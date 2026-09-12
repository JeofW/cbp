using System.Collections.Generic;

namespace WholesomeAQ
{
    public class WholesomeAQSettings
    {
        public int ScanStartDistance { get; set; } = 250;
        public int ScanStep { get; set; } = 250;
        public int ScanMaxDistance { get; set; } = 4000;
        public int MaxQuestsPerProfile { get; set; } = 20;
        public int MinQuestLevelOffset { get; set; } = 7;
        public bool EnableAutoVendor { get; set; } = true;
        public bool EnableAutoTrain { get; set; } = true;
        public bool SellWhite { get; set; } = false;
        public bool SellGreen { get; set; } = false;
        public bool SellBlue { get; set; } = false;
        public int RestHealthPercent { get; set; } = 45;
        public int RestManaPercent { get; set; } = 30;
        public int RestResumeHealthPercent { get; set; } = 75;
        public int RestResumeManaPercent { get; set; } = 60;
        public HashSet<int> BlacklistedVendors { get; set; } = new HashSet<int>();

        public string VendorBlacklistText
        {
            get => string.Join(",", BlacklistedVendors);
            set
            {
                BlacklistedVendors = new HashSet<int>();
                if (string.IsNullOrWhiteSpace(value)) return;
                foreach (string part in value.Split(','))
                {
                    if (int.TryParse(part.Trim(), out int id))
                        BlacklistedVendors.Add(id);
                }
            }
        }
    }
}
