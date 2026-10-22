using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/rooms"), Authorize(Roles = Roles.Admin)]
public class RoomsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet][ProducesResponseType(typeof(ResponseEnvelope<PageResult<Room>>), 200)] public async Task<IActionResult> List([FromQuery] ListQuery p) { var q = db.Rooms.AsNoTracking(); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.Code.Contains(p.Search) || x.Name.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderBy(x => x.Id).PageAsync(p))); }
    [HttpGet("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Room>), 200)] public async Task<IActionResult> Get(int id) => Ok(ApiResponse.Ok(await db.Rooms.AsNoTracking().Where(x => x.Id == id).Required()));
    [HttpPost][ProducesResponseType(typeof(ResponseEnvelope<Room>), 201)] public async Task<IActionResult> Create(RoomRequest r) { var entity = new Room { Code = r.Code.Trim(), Name = r.Name.Trim(), Latitude = r.Latitude, Longitude = r.Longitude, AllowedRadiusMeters = r.AllowedRadiusMeters }; db.Rooms.Add(entity); await db.SaveChangesAsync(); return CreatedAtAction(nameof(Get), new { id = entity.Id }, ApiResponse.Ok(entity)); }
    [HttpPut("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Room>), 200)] public async Task<IActionResult> Update(int id, RoomRequest r) { var entity = await db.Rooms.Where(x => x.Id == id).Required(); ApiSupport.Require(!await db.Sessions.AnyAsync(x => x.RoomId == id), "Phòng đã được dùng trong buổi học.", 409); entity.Code = r.Code.Trim(); entity.Name = r.Name.Trim(); entity.Latitude = r.Latitude; entity.Longitude = r.Longitude; entity.AllowedRadiusMeters = r.AllowedRadiusMeters; await db.SaveChangesAsync(); return Ok(ApiResponse.Ok(entity)); }
    [HttpDelete("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<object>), 200)] public async Task<IActionResult> Delete(int id) { var entity = await db.Rooms.Where(x => x.Id == id).Required(); ApiSupport.Require(!(await db.Sessions.AnyAsync(x => x.RoomId == id)), "Dữ liệu đã được sử dụng; không thể xóa.", 409); db.Rooms.Remove(entity); await db.SaveChangesAsync(); return Ok(ApiResponse.Ok()); }
}
