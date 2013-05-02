﻿namespace Soundfingerprinting.Models
{
    public class Fingerprint
    {
        public int OrderNumber { get; set; }

        public int StartAtSecond { get; set; }

        public int EndAtSecond { get; set; }

        public bool[] Signature { get; set; }
    }
}
