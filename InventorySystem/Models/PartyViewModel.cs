namespace InventorySystem.Models
{
    public class PartyViewModel
    {
        public int PartyId { get; set; }
        public string? PartyCode { get; set; }
        public string? PartyType { get; set; } // supplier, customer, both
        public string PartyName { get; set; }
        public string? ContactPerson { get; set; }
        public string? Designation { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public decimal? OpeningBalance { get; set; }
        public decimal? CreditLimit { get; set; }
        public int? PaymentTerms { get; set; }
        public string? TaxId { get; set; }
        public string? Notes { get; set; }
        public string? Status { get; set; } // active / inactive
    }
}
