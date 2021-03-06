﻿namespace SoundFingerprinting
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.DAO;
    using SoundFingerprinting.Data;
    using SoundFingerprinting.LCS;
    using SoundFingerprinting.Math;
    using SoundFingerprinting.Query;

    internal class QueryFingerprintService : IQueryFingerprintService
    {
        private readonly ISimilarityUtility similarityUtility;
        private readonly IQueryMath queryMath;

        private static readonly QueryFingerprintService Singleton = new QueryFingerprintService(
            new SimilarityUtility(),
            new QueryMath(new QueryResultCoverageCalculator(), new ConfidenceCalculator()));

        public static QueryFingerprintService Instance
        {
            get
            {
                return Singleton;
            }
        }

        internal QueryFingerprintService(ISimilarityUtility similarityUtility, IQueryMath queryMath)
        {
            this.similarityUtility = similarityUtility;
            this.queryMath = queryMath;
        }
    
        public QueryResult Query(List<HashedFingerprint> queryFingerprints, QueryConfiguration configuration, IModelService modelService)
        {
            ConcurrentDictionary<IModelReference, ResultEntryAccumulator> hammingSimilarities;
            if (modelService.SupportsBatchedSubFingerprintQuery)
            {
                hammingSimilarities = GetSimilaritiesUsingBatchedStrategy(queryFingerprints, configuration, modelService);
            }
            else
            {
                hammingSimilarities = GetSimilaritiesUsingNonBatchedStrategy(queryFingerprints, configuration, modelService);
            }

            if (!hammingSimilarities.Any())
            {
                return QueryResult.EmptyResult();
            }

            var resultEntries = queryMath.GetBestCandidates(queryFingerprints, hammingSimilarities, configuration.MaxTracksToReturn, modelService, configuration.FingerprintConfiguration);
            int totalTracksAnalyzed = hammingSimilarities.Count;
            int totalSubFingerprintsAnalyzed = hammingSimilarities.Values.Select(entry => entry.Matches.Count).Sum();
            return QueryResult.NonEmptyResult(resultEntries, totalTracksAnalyzed, totalSubFingerprintsAnalyzed);
        }

        private ConcurrentDictionary<IModelReference, ResultEntryAccumulator> GetSimilaritiesUsingNonBatchedStrategy(IEnumerable<HashedFingerprint> queryFingerprints, QueryConfiguration configuration, IModelService modelService)
        {
            var hammingSimilarities = new ConcurrentDictionary<IModelReference, ResultEntryAccumulator>();

            Parallel.ForEach(queryFingerprints, queryFingerprint => 
            { 
                var subFingerprints = modelService.ReadSubFingerprints(queryFingerprint.HashBins, configuration);
                similarityUtility.AccumulateHammingSimilarity(subFingerprints, queryFingerprint, hammingSimilarities, configuration.FingerprintConfiguration.HashingConfig.NumberOfMinHashesPerTable);
            });

            return hammingSimilarities;
        }

        private ConcurrentDictionary<IModelReference, ResultEntryAccumulator> GetSimilaritiesUsingBatchedStrategy(IEnumerable<HashedFingerprint> queryFingerprints, QueryConfiguration configuration, IModelService modelService)
        {
            var hashedFingerprints = queryFingerprints as List<HashedFingerprint> ?? queryFingerprints.ToList();
            var allCandidates = modelService.ReadSubFingerprints(hashedFingerprints.Select(querySubfingerprint => querySubfingerprint.HashBins), configuration);
            var hammingSimilarities = new ConcurrentDictionary<IModelReference, ResultEntryAccumulator>();
            Parallel.ForEach(hashedFingerprints, queryFingerprint => 
            {
                var subFingerprints = allCandidates.Where(candidate => queryMath.IsCandidatePassingThresholdVotes(queryFingerprint, candidate, configuration.ThresholdVotes));
                similarityUtility.AccumulateHammingSimilarity(subFingerprints, queryFingerprint, hammingSimilarities, configuration.FingerprintConfiguration.HashingConfig.NumberOfMinHashesPerTable);
            });

            return hammingSimilarities;
        }
    }
}
