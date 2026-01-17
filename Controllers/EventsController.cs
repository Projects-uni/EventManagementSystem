using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventManagementSystem.Models;
using EventManagementSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Supabase;
using static Supabase.Postgrest.Constants;

using Microsoft.AspNetCore.Mvc;
using static Supabase.Postgrest.Constants;
using EventManagementSystem.Models;

namespace EventManagementSystem.Controllers
{
    public class EventsController : Controller
    {
        private readonly Client _supabase;

        public EventsController(SupabaseService supabaseService)
        {
            _supabase = supabaseService.Client;
        }


        private bool IsUserLoggedIn()
        {
            return HttpContext.Session.GetString("UserId") != null;
        }

        private Guid? GetCurrentUserId()
        {
            var idStr = HttpContext.Session.GetString("UserId");
            return Guid.TryParse(idStr, out var id) ? id : null;
        }

        private string GetCurrentUserRole()
        {
            return HttpContext.Session.GetString("Role") ?? string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Auth");

    var role = GetCurrentUserRole();
    var events = new List<Event>();

    try
    {
        if (role == "Admin")
        {
            var resp = await _supabase
                .From<Event>()
                .Order(e => e.CreatedAt, Ordering.Descending)
                .Get();

            events = resp.Models;
        }
        else if (role == "Organizer")
        {
            // Own events
            var ownResp = await _supabase
                .From<Event>()
                .Where(e => e.OrganizerId == userId.Value)
                .Order(e => e.CreatedAt, Ordering.Descending)
                .Get();

            var ownEvents = ownResp.Models;

            // Invited events
            var partResp = await _supabase
                .From<Participant>()
                .Where(p => p.UserId == userId.Value)
                .Get();

            var invitedEventIds = partResp.Models
                .Select(p => p.EventId)
                .Distinct()
                .ToArray();

            var invitedEvents = new List<Event>();
            if (invitedEventIds.Length > 0)
            {
                var invitedResp = await _supabase
                    .From<Event>()
                    .Filter("id", Operator.In, invitedEventIds)
                    .Get();

                invitedEvents = invitedResp.Models;
            }

            events = ownEvents
                .Concat(invitedEvents)
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
        }
        else // Guest
        {
            var partResp = await _supabase
                .From<Participant>()
                .Where(p => p.UserId == userId.Value)
                .Get();

            var invitedEventIds = partResp.Models
                .Select(p => p.EventId)
                .Distinct()
                .ToArray();

            if (invitedEventIds.Length > 0)
            {
                var invitedResp = await _supabase
                    .From<Event>()
                    .Filter("id", Operator.In, invitedEventIds)
                    .Order(e => e.CreatedAt, Ordering.Descending)
                    .Get();

                events = invitedResp.Models;
            }
        }

        

        // ============================
        // Organizer lookup
        // ============================
        var organizerIds = events
            .Select(e => e.OrganizerId)
            .Distinct()
            .ToArray();

        if (organizerIds.Length > 0)
        {
            var usersResp = await _supabase
                .From<User>()
                .Filter("id", Operator.In, organizerIds)
                .Get();

            var usersById = usersResp.Models.ToDictionary(u => u.Id, u => u);

            foreach (var ev in events)
            {
                if (usersById.TryGetValue(ev.OrganizerId, out var org))
                    ev.Organizer = org;
            }
        }

        // ============================
        // Category & Location lookup
        // ============================
        var categoriesResp = await _supabase
            .From<Category>()
            .Get();

        var locationsResp = await _supabase
            .From<Location>()
            .Get();

        ViewBag.CategoryMap = categoriesResp.Models
            .ToDictionary(c => c.Id, c => c.Name);

        ViewBag.LocationMap = locationsResp.Models
            .ToDictionary(
                l => l.Id,
                l => $"{l.Name}{(string.IsNullOrWhiteSpace(l.City) ? "" : " - " + l.City)}"
            );
    }
    catch (Exception ex)
    {
        ViewBag.Error = $"Error loading events: {ex.Message}";
    }

    ViewBag.UserRole = role;
    ViewBag.Username = HttpContext.Session.GetString("Username");

    return View(events);
}

        [HttpGet]
        public IActionResult Create()
        {
            if (!IsUserLoggedIn())
                return RedirectToAction("Login", "Auth");

            var role = GetCurrentUserRole();
            if (role == "Guest")
                return RedirectToAction("Index");

            ViewBag.Username = HttpContext.Session.GetString("Username");
            return View();
        }

       [HttpPost]
public async Task<IActionResult> Create(Event model)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var role = GetCurrentUserRole();
    if (role == "Guest")
        return RedirectToAction("Index");

    var userId = GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Auth");

    try
    {
        // basic server-side validation
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ViewBag.Error = "Etkinlik adı zorunludur.";
            return View(model);
        }

        if (model.EndDate < model.StartDate)
        {
            ViewBag.Error = "Bitiş tarihi başlangıç tarihinden önce olamaz.";
            return View(model);
        }

        var newEvent = new Event
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            Description = model.Description,

            // Old string fields (still ok to keep for note / legacy)
            Category = model.Category,
            Location = model.Location,

            StartDate = model.StartDate,
            EndDate = model.EndDate,

            Status = string.IsNullOrWhiteSpace(model.Status) ? "Upcoming" : model.Status,
            Budget = model.Budget,

            OrganizerId = userId.Value,
            CreatedAt = DateTime.UtcNow,

            // ✅ MAIN FIX: save relationships
            CategoryId = model.CategoryId,
            LocationId = model.LocationId
        };

        await _supabase
            .From<Event>()
            .Insert(newEvent);

        return RedirectToAction("Index");
    }
    catch (Exception ex)
    {
        ViewBag.Error = $"Error creating event: {ex.Message}";
        return View(model);
    }
}

[HttpGet]
public async Task<IActionResult> GetNotifications(Guid id)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    try
    {
        var resp = await _supabase
            .From<EventNotification>()
            .Where(n => n.EventId == id)
            .Order(n => n.CreatedAt, Ordering.Descending)
            .Get();

        // ✅ Convert to plain objects (serializable)
        var result = resp.Models.Select(n => new
        {
            id = n.Id,
            eventId = n.EventId,
            createdBy = n.CreatedBy,
            title = n.Title,
            message = n.Message,
            createdAt = n.CreatedAt
        });

        return Ok(result);
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
    }
}


        [HttpGet]
       public async Task<IActionResult> Details(Guid id)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    Event? eventItem = null;

    try
    {
        var resp = await _supabase
            .From<Event>()
            .Where(e => e.Id == id)
            .Limit(1)
            .Get();

        eventItem = resp.Models.FirstOrDefault();

        if (eventItem != null)
        {
            // Organizer info
            var orgResp = await _supabase
                .From<User>()
                .Where(u => u.Id == eventItem.OrganizerId)
                .Limit(1)
                .Get();

            eventItem.Organizer = orgResp.Models.FirstOrDefault();

            // ✅ NEW: load invitations for this event
            var invResp = await _supabase
                .From<Invitation>()
                .Where(i => i.EventId == id)
                .Get();

            // Pass to view (you can list them in Details.cshtml)
            ViewBag.Invitations = invResp.Models;
        }
    }
    catch (Exception ex)
    {
        ViewBag.Error = $"Error loading event: {ex.Message}";
    }

    if (eventItem == null)
        return NotFound();

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    ViewBag.UserRole = role;
    ViewBag.UserId = userId;
    ViewBag.Username = HttpContext.Session.GetString("Username");
    ViewBag.CanEdit = role == "Admin" || (userId != null && eventItem.OrganizerId == userId.Value);

    return View(eventItem);
}


[HttpGet]
public async Task<IActionResult> CreateNotification(Guid eventId)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Auth");

    var role = GetCurrentUserRole();
    if (role != "Organizer" && role != "Admin")
        return Forbid();

    // Load event (for title / validation)
    var evResp = await _supabase
        .From<Event>()
        .Where(e => e.Id == eventId)
        .Limit(1)
        .Get();

    var ev = evResp.Models.FirstOrDefault();
    if (ev == null) return NotFound();

    // Organizer can only post to own event (Admin can post to any)
    if (role != "Admin" && ev.OrganizerId != userId.Value)
        return Forbid();

    ViewBag.EventId = eventId;
    ViewBag.EventName = ev.Name;
    ViewBag.Username = HttpContext.Session.GetString("Username");

    return View();
}



[HttpPost]
public async Task<IActionResult> CreateNotification(Guid eventId, string title, string message)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Auth");

    var role = GetCurrentUserRole();
    if (role != "Organizer" && role != "Admin")
        return Forbid();

    // basic validation
    title = (title ?? "").Trim();
    message = (message ?? "").Trim();

    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
    {
        ViewBag.Error = "Başlık ve mesaj zorunludur.";
        ViewBag.EventId = eventId;
        return View();
    }

    // 1) Load event & auth check
    var evResp = await _supabase
        .From<Event>()
        .Where(e => e.Id == eventId)
        .Limit(1)
        .Get();

    var ev = evResp.Models.FirstOrDefault();
    if (ev == null) return NotFound();

    if (role != "Admin" && ev.OrganizerId != userId.Value)
        return Forbid();

    try
    {
        // 2) Insert notification
        var notif = new EventNotification
        {
            EventId = eventId,
            CreatedBy = userId.Value,
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        var notifResp = await _supabase
            .From<EventNotification>()
            .Insert(notif);

        var created = notifResp.Models.FirstOrDefault();
        if (created == null)
        {
            TempData["Error"] = "Bildirim oluşturulamadı.";
            return RedirectToAction("Details", new { id = eventId });
        }

        // 3) Load participants (recipients)
        var partResp = await _supabase
            .From<Participant>()
            .Where(p => p.EventId == eventId)
            .Get();

        var recipientIds = partResp.Models
            .Select(p => p.UserId)
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .Where(uid => uid != userId.Value) // optional: don't notify yourself
            .ToList();

        // 4) Bulk insert recipients
        if (recipientIds.Count > 0)
        {
            var recRows = recipientIds.Select(uid => new NotificationRecipient
            {
                NotificationId = created.Id,
                UserId = uid,
                IsRead = false,
                ReadAt = null,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _supabase
                .From<NotificationRecipient>()
                .Insert(recRows);
        }

        TempData["Success"] = "Bildirim yayınlandı.";
        return RedirectToAction("Details", new { id = eventId });
    }
    catch (Exception ex)
    {
        TempData["Error"] = $"Bildirim hatası: {ex.Message}";
        return RedirectToAction("Details", new { id = eventId });
    }
}



[HttpGet]
public async Task<IActionResult> GetTasks(Guid id)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    try
    {
        var tasksResp = await _supabase
            .From<Models.Task>()
            .Where(t => t.EventId == id)
            .Order(t => t.CreatedAt, Ordering.Descending)
            .Get();

        var tasks = tasksResp.Models;

        // Load assigned users
        var userIds = tasks
            .Where(t => t.AssignedTo.HasValue)
            .Select(t => t.AssignedTo!.Value)
            .Distinct()
            .ToArray();

        var usersById = new Dictionary<Guid, User>();

        if (userIds.Length > 0)
        {
            var usersResp = await _supabase
                .From<User>()
                .Filter("id", Operator.In, userIds)
                .Get();

            usersById = usersResp.Models.ToDictionary(u => u.Id, u => u);
        }

        // Project to DTO so frontend gets assignedUser but Supabase never sees it
        var result = tasks.Select(t =>
        {
            User? u = null;
            if (t.AssignedTo.HasValue)
                usersById.TryGetValue(t.AssignedTo.Value, out u);

            return new
            {
                id = t.Id,
                eventId = t.EventId,
                name = t.Name,
                description = t.Description,
                dueDate = t.DueDate,
                priority = t.Priority,
                budget = t.Budget,
                comment = t.Comment,
                status = t.Status,
                assignedTo = t.AssignedTo,
                assignedUser = u == null
                    ? null
                    : new { username = u.Username, email = u.Email }
            };
        });

        return Json(result);
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

public class UpdateTaskRequest
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
}




// POST: /Events/UpdateTask
[HttpPost]
public async Task<IActionResult> UpdateTask([FromBody] UpdateTaskRequest request)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return Unauthorized();

    try
    {
        // 1) Load task
        var taskResp = await _supabase
            .From<Models.Task>()
            .Where(t => t.Id == request.Id)
            .Limit(1)
            .Get();

        var task = taskResp.Models.FirstOrDefault();
        if (task == null)
            return NotFound(new { error = "Task not found." });

        // 2) Load event to check organizer
        var eventResp = await _supabase
            .From<Event>()
            .Where(e => e.Id == task.EventId)
            .Limit(1)
            .Get();

        var ev = eventResp.Models.FirstOrDefault();
        if (ev == null)
            return NotFound(new { error = "Event not found." });

        // 3) Permissions:
        //  - Admin: everything
        //  - Organizer of this event: everything
        //  - Assigned user: can edit their own task
        var canEdit =
            role == "Admin" ||
            ev.OrganizerId == userId.Value ||
            (task.AssignedTo.HasValue && task.AssignedTo.Value == userId.Value);

        if (!canEdit)
            return Forbid();

        // 4) Update fields (we only expose status + comment for now)
        if (!string.IsNullOrWhiteSpace(request.Status))
            task.Status = request.Status;

        task.Comment = request.Comment;

        await _supabase
            .From<Models.Task>()
            .Update(task);

        return Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}



        // POST: /Events/AddTask
[HttpPost]
public async Task<IActionResult> AddTask([FromBody] Models.Task task)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return Unauthorized();

    try
    {
        var eventResp = await _supabase
            .From<Event>()
            .Where(e => e.Id == task.EventId)
            .Limit(1)
            .Get();

        var eventItem = eventResp.Models.FirstOrDefault();
        if (eventItem == null)
            return NotFound("Event not found.");

        if (role != "Admin" && eventItem.OrganizerId != userId.Value)
            return Forbid("You cannot add tasks to this event.");

        if (task.AssignedTo.HasValue)
        {
            var participantResp = await _supabase
                .From<Participant>()
                .Where(p => p.EventId == task.EventId)
                .Where(p => p.UserId == task.AssignedTo.Value)
                .Get();

            if (!participantResp.Models.Any())
            {
                return BadRequest(new
                {
                    error = "You can assign this task only to participants. Invite the user first."
                });
            }
        }

        task.Id = Guid.NewGuid();
        task.CreatedAt = DateTime.UtcNow;
        task.Status = "Pending";

        await _supabase
            .From<Models.Task>()
            .Insert(task);

        return Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

[HttpGet]
public async Task<IActionResult> GetParticipants(Guid id)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    try
    {
        var participantsResp = await _supabase
            .From<Participant>()
            .Where(p => p.EventId == id)
            .Order(p => p.InvitedAt, Ordering.Descending)
            .Get();

        var participants = participantsResp.Models;

        var userIds = participants
            .Select(p => p.UserId)
            .Distinct()
            .ToArray();

        var usersById = new Dictionary<Guid, User>();

        if (userIds.Length > 0)
        {
            var usersResp = await _supabase
                .From<User>()
                .Filter("id", Operator.In, userIds)
                .Get();

            usersById = usersResp.Models.ToDictionary(u => u.Id, u => u);
        }

        // Project to DTO: we explicitly build "user" object
        var result = participants.Select(p =>
        {
            usersById.TryGetValue(p.UserId, out var u);

            return new
            {
                id = p.Id,
                eventId = p.EventId,
                userId = p.UserId,
                role = p.Role,
                canEdit = p.CanEdit,
                invitedAt = p.InvitedAt,
                user = u == null
                    ? null
                    : new { username = u.Username, email = u.Email }
            };
        });

        return Json(result);
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

        // POST: /Events/AddParticipant
        [HttpPost]
public async Task<IActionResult> AddParticipant([FromBody] AddParticipantRequest request)
{
    if (!IsUserLoggedIn())
        return Unauthorized();

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return Unauthorized();

    try
    {
        // event check
        var eventResp = await _supabase
            .From<Event>()
            .Where(e => e.Id == request.EventId)
            .Limit(1)
            .Get();

        var eventItem = eventResp.Models.FirstOrDefault();
        if (eventItem == null)
            return NotFound("Event not found.");

        if (role != "Admin" && eventItem.OrganizerId != userId.Value)
            return Forbid("You cannot invite participants to this event.");

        // find user BY USERNAME (unique)
        var userResp = await _supabase
            .From<User>()
            .Where(u => u.Username == request.Username)
            .Limit(1)
            .Get();

        var invitedUser = userResp.Models.FirstOrDefault();
        if (invitedUser == null)
            return BadRequest(new { error = "User with this username does not exist." });

        // check existing invitation
        var checkResp = await _supabase
            .From<Participant>()
            .Where(p => p.EventId == request.EventId)
            .Where(p => p.UserId == invitedUser.Id)
            .Get();

        if (checkResp.Models.Any())
            return BadRequest(new { error = "This user is already a participant." });

        // insert new participant
        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            EventId = request.EventId,
            UserId = invitedUser.Id,
            InvitedAt = DateTime.UtcNow
        };

        await _supabase.From<Participant>().Insert(participant);

        return Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

public class AddParticipantRequest
{
    public Guid EventId { get; set; }
    public string Username { get; set; } = string.Empty;
}

        // GET: /Events/GetAvailableUsers/id
        [HttpGet]
        public async Task<IActionResult> GetAvailableUsers(Guid id)
        {
            if (!IsUserLoggedIn())
                return Unauthorized();

            try
            {
                var participantsResp = await _supabase
                    .From<Participant>()
                    .Where(p => p.EventId == id)
                    .Get();

                var participantUserIds = participantsResp.Models
                    .Select(p => p.UserId)
                    .ToHashSet();

                var eventResp = await _supabase
                    .From<Event>()
                    .Filter("id", Operator.Equals, id)
                    .Limit(1)
                    .Get();

                var eventItem = eventResp.Models.FirstOrDefault();
                if (eventItem != null)
                    participantUserIds.Add(eventItem.OrganizerId);

                var usersResp = await _supabase
                    .From<User>()
                    .Get();

                var availableUsers = usersResp.Models
                    .Where(u => !participantUserIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Username, u.Email })
                    .ToList();

                return Json(availableUsers);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET: /Events/Delete/id
        // GET: /Events/Delete/id
[HttpGet]
public async Task<IActionResult> Delete(Guid id)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return RedirectToAction("Login", "Auth");

    try
    {
        // Load event
        var resp = await _supabase
            .From<Event>()
            .Where(e => e.Id == id)
            .Limit(1)
            .Get();

        var ev = resp.Models.FirstOrDefault();
        if (ev == null)
            return NotFound();

        // Permission: only Admin or this event's organizer
        if (role != "Admin" && ev.OrganizerId != userId.Value)
            return Forbid();

        // 1) Delete tasks of this event
        await _supabase
            .From<Models.Task>()
            .Where(t => t.EventId == id)
            .Delete();

        // 2) Delete participants of this event
        await _supabase
            .From<Participant>()
            .Where(p => p.EventId == id)
            .Delete();

        // 3) Delete the event itself
        await _supabase
            .From<Event>()
            .Where(e => e.Id == id)
            .Delete();
    }
    catch (Exception ex)
    {
        TempData["Error"] = $"Error deleting event: {ex.Message}";
    }

    return RedirectToAction("Index");
}

// GET: /Events/Edit/id
[HttpGet]
public async Task<IActionResult> Edit(Guid id)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return RedirectToAction("Login", "Auth");

    try
    {
        var resp = await _supabase
            .From<Event>()
            .Where(e => e.Id == id)
            .Limit(1)
            .Get();

        var ev = resp.Models.FirstOrDefault();
        if (ev == null)
            return NotFound();

        if (role != "Admin" && ev.OrganizerId != userId.Value)
            return Forbid();

        ViewBag.UserRole = role;
        ViewBag.Username = HttpContext.Session.GetString("Username");
        return View(ev);
    }
    catch (Exception ex)
    {
        TempData["Error"] = $"Error loading event: {ex.Message}";
        return RedirectToAction("Index");
    }
}

// POST: /Events/Edit
[HttpPost]
public async Task<IActionResult> Edit(Event model)
{
    if (!IsUserLoggedIn())
        return RedirectToAction("Login", "Auth");

    var userId = GetCurrentUserId();
    var role = GetCurrentUserRole();

    if (userId == null)
        return RedirectToAction("Login", "Auth");

    try
    {
        // Load existing event
        var resp = await _supabase
            .From<Event>()
            .Where(e => e.Id == model.Id)
            .Limit(1)
            .Get();

        var ev = resp.Models.FirstOrDefault();
        if (ev == null)
            return NotFound();

        if (role != "Admin" && ev.OrganizerId != userId.Value)
            return Forbid();

        // Update editable fields
        ev.Name = model.Name;
        ev.Description = model.Description;
        ev.Category = model.Category;
        ev.StartDate = model.StartDate;
        ev.EndDate = model.EndDate;
        ev.Location = model.Location;
        ev.Status = model.Status;
        ev.Budget = model.Budget;

        await _supabase
            .From<Event>()
            .Update(ev);

        return RedirectToAction("Details", new { id = ev.Id });
    }
    catch (Exception ex)
    {
        ViewBag.Error = $"Error updating event: {ex.Message}";
        ViewBag.UserRole = role;
        ViewBag.Username = HttpContext.Session.GetString("Username");
        return View(model);
    }
}

    }

}
