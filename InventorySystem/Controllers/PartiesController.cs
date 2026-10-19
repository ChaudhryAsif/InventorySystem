using InventorySystem.Core.Models;
using InventorySystem.Data;
using InventorySystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Controllers
{
    [Authorize]
    public class PartiesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PartiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public ActionResult Index()
        {
            return View();
        }

        // GET: api/Parties
        [HttpGet]
        public async Task<IActionResult> GetParties(int page = 1, int pageSize = 50)
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            var totalCount = await _context.Parties.CountAsync();

            var data = await _context.Parties
                .OrderBy(p => p.PartyName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new PartyViewModel
                {
                    PartyId = p.PartyId,
                    PartyCode = p.PartyCode,
                    PartyType = p.PartyType,
                    PartyName = p.PartyName,
                    ContactPerson = p.ContactPerson,
                    Designation = p.Designation,
                    Mobile = p.Mobile,
                    Website = p.Website,
                    Address = p.Address,
                    City = p.City,
                    State = p.State,
                    ZipCode = p.ZipCode,
                    Country = p.Country,
                    CreditLimit = p.CreditLimit,
                    PaymentTerms = p.PaymentTerms,
                    TaxId = p.TaxId,
                    Notes = p.Notes,
                    Phone = p.Phone,
                    Email = p.Email,
                    OpeningBalance = p.OpeningBalance,
                    Status = p.Status
                })
                .ToListAsync();

            return Ok(new { data, totalCount, page, pageSize });
        }

        [HttpGet]
        public async Task<ActionResult<PartyViewModel>> GetById(int id)
        {
            var party = await _context.Parties.FindAsync(id);

            if (party == null)
                return NotFound();

            var partyViewModel = new PartyViewModel
            {
                PartyId = party.PartyId,
                PartyCode = party.PartyCode,
                PartyType = party.PartyType,
                PartyName = party.PartyName,
                ContactPerson = party.ContactPerson,
                    Designation = party.Designation,
                    Mobile = party.Mobile,
                    Website = party.Website,
                    Address = party.Address,
                    City = party.City,
                    State = party.State,
                    ZipCode = party.ZipCode,
                    Country = party.Country,
                    CreditLimit = party.CreditLimit,
                    PaymentTerms = party.PaymentTerms,
                    TaxId = party.TaxId,
                    Notes = party.Notes,
                Phone = party.Phone,
                Email = party.Email,
                OpeningBalance = party.OpeningBalance,
                Status = party.Status
            };

            return Ok(partyViewModel);
        }

        // POST: api/Parties
        [HttpPost]
        public async Task<ActionResult> save([FromBody] PartyViewModel model)
        {
            try
            {
                if (model == null)
                    return BadRequest("Invalid party data.");
                model.PartyName = model.PartyName?.Trim() ?? "";
                model.PartyType = model.PartyType?.Trim().ToLowerInvariant();
                model.Status = model.Status?.Trim().ToLowerInvariant();
                if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.PartyName) ||
                    model.PartyType is not ("supplier" or "customer" or "both") ||
                    model.Status is not ("active" or "inactive"))
                    return BadRequest(new { success = false, message = "Enter a party name, valid party type, and active or inactive status." });
                if (await _context.Parties.AnyAsync(p => p.PartyId != model.PartyId && p.PartyName.Trim().ToLower() == model.PartyName.ToLower()))
                    return Conflict(new { success = false, message = "A party with the same name already exists." });

                var party = new Party();

                if (model.PartyId == 0)
                {
                    var exists = await _context.Parties
                        .AnyAsync(p => p.PartyName.ToLower() == model.PartyName.ToLower());
                    if (exists)
                        return Conflict("A party with the same name already exists.");
                    var newCode = GeneratePartyCode(model.PartyType);

                    party = new Party
                    {
                        PartyCode = newCode,
                        PartyType = model.PartyType,
                        PartyName = model.PartyName,
                        ContactPerson = model.ContactPerson,
                    Designation = model.Designation,
                    Mobile = model.Mobile,
                    Website = model.Website,
                    Address = model.Address,
                    City = model.City,
                    State = model.State,
                    ZipCode = model.ZipCode,
                    Country = model.Country,
                    CreditLimit = model.CreditLimit,
                    PaymentTerms = model.PaymentTerms,
                    TaxId = model.TaxId,
                    Notes = model.Notes,
                        Phone = model.Phone,
                        Email = model.Email,
                        OpeningBalance = model.OpeningBalance,
                        Status = model.Status
                    };

                    _context.Parties.Add(party);
                }
                else
                {
                    party = await _context.Parties.FindAsync(model.PartyId);
                    if (party == null)
                        return NotFound("Party not found.");
                    party.PartyType = model.PartyType;
                    party.PartyName = model.PartyName;
                    party.ContactPerson = model.ContactPerson;
                    party.Designation = model.Designation;
                    party.Mobile = model.Mobile;
                    party.Website = model.Website;
                    party.Address = model.Address;
                    party.City = model.City;
                    party.State = model.State;
                    party.ZipCode = model.ZipCode;
                    party.Country = model.Country;
                    party.CreditLimit = model.CreditLimit;
                    party.PaymentTerms = model.PaymentTerms;
                    party.TaxId = model.TaxId;
                    party.Notes = model.Notes;
                    party.Phone = model.Phone;
                    party.Email = model.Email;
                    party.OpeningBalance = model.OpeningBalance;
                    party.Status = model.Status;
                }

                await _context.SaveChangesAsync();

                return Ok(party);
            }
            catch (Exception)
            {
                throw;
            }
        }

        // 🔹 DELETE: api/Parties/5
        [HttpDelete]
        public async Task<ActionResult> DeleteParty(int id)
        {
            var party = await _context.Parties.FindAsync(id);
            if (party == null)
                return NotFound();

            party.Status = "inactive";
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // 🔹 Generate Party Code (Auto Increment)
        private string GeneratePartyCode(string type)
        {
            string prefix = type.ToLower() switch
            {
                "supplier" => "SUP",
                "customer" => "CUS",
                "both" => "BOT",
                _ => "PTY"
            };

            int count = _context.Parties.Count(p => p.PartyType == type) + 1;
            return $"{prefix}-{count.ToString("D4")}";
        }

        // GET: Get all suppliers for lookup
        [HttpGet]
        public IActionResult GetSuppliers()
        {
            try
            {
                var suppliers = _context.Parties
                    .Where(p => (p.PartyType == "supplier" || p.PartyType == "both")
                             && p.Status == "active")
                    .Select(p => new
                    {
                        p.PartyId,
                        p.PartyName,
                        p.PartyType,
                        p.ContactPerson,
                        p.Phone,
                        p.Email,
                        p.Address,
                        p.OpeningBalance
                    })
                    .OrderBy(p => p.PartyName)
                    .ToList();

                return Json(new { success = true, suppliers = suppliers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Get all customers for sale invoice lookup
        [HttpGet]
        public IActionResult GetCustomers()
        {
            try
            {
                var customers = _context.Parties
                    .Where(p => (p.PartyType == "customer" || p.PartyType == "both")
                             && p.Status == "active")
                    .Select(p => new
                    {
                        p.PartyId,
                        p.PartyName,
                        p.PartyType,
                        p.ContactPerson,
                        p.Phone,
                        p.Email,
                        p.Address,
                        p.OpeningBalance
                    })
                    .OrderBy(p => p.PartyName)
                    .ToList();

                return Json(new { success = true, customers = customers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
