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
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (model == null)
                    return BadRequest("Invalid party data.");

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

            _context.Parties.Remove(party);
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
