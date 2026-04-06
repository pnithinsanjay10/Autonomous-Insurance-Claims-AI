namespace InsuranceClaimsAI.Models
{
    public class ClaimResult
    {
        public ExtractedFields extractedFields { get; set; }
        public List<string> missingFields { get; set; }
        public string recommendedRoute { get; set; }
        public string reasoning { get; set; }
    }

    public class ExtractedFields
    {
        // Policy Information
        public string PolicyNumber { get; set; }
        public string PolicyholderName { get; set; }
        public string EffectiveDate { get; set; }

        // Incident Information
        public string IncidentDate { get; set; }
        public string IncidentTime { get; set; }
        public string IncidentLocation { get; set; }
        public string IncidentDescription { get; set; }

        // Involved Parties
        public string Claimant { get; set; }
        public string ThirdParties { get; set; }
        public string ContactDetails { get; set; }

        // Asset Details
        public string AssetType { get; set; }
        public string AssetId { get; set; }
        public decimal EstimatedDamage { get; set; }

        // Other Mandatory Fields
        public string ClaimType { get; set; }
        public string Attachments { get; set; }
        public string InitialEstimate { get; set; }
    }
}