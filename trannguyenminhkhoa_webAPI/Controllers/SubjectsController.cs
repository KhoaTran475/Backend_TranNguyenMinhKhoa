using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/subjects"), Authorize(Roles = Roles.Admin)]
public class SubjectsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet][ProducesResponseType(typeof(ResponseEnvelope<PageResult<Subject>>), 200)] public async Task<IActionResult> List([FromQuery] ListQuery p) { var q = db.Subjects.AsNoTracking(); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.Code.Contains(p.Search) || x.Name.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderBy(x => x.Id).PageAsync(p))); }
    [HttpGet("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Subject>), 200)] public async Task<IActionResult> Get(int id) => Ok(ApiResponse.Ok(await db.Subjects.AsNoTracking().Where(x => x.Id == id).Required()));
    [HttpPost][ProducesResponseType(typeof(ResponseEnvelope<Subject>), 201)] public async Task<IActionResult> Create(SubjectRequest r) { var entity = new Subject { Code = r.Code.Trim(), Name = r.Name.Trim() }; db.Subjects.Add(entity); await db.SaveChangesAsync(); return CreatedAtAction(nameof(Get), new { id = entity.Id }, ApiResponse.Ok(entity)); }
    [HttpPut("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Subject>), 200)] public async Task<IActionResult> Update(int id, SubjectRequest r) { var entity = await db.Subjects.Where(x => x.Id == id).Required(); entity.Code = r.Code.Trim(); entity.Name = r.Name.Trim(); await db.SaveChangesAsync(); return Ok(ApiResponse.Ok(entity)); }
    [HttpDelete("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<object>), 200)] public async Task<IActionResult> Delete(int id) { var entity = await db.Subjects.Where(x => x.Id == id).Required(); ApiSupport.Require(!(await db.Classes.AnyAsync(x => x.SubjectId == id)), "Dữ liệu đã được sử dụng; không thể xóa.", 409); db.Subjects.Remove(entity); await db.SaveChangesAsync(); return Ok(ApiResponse.Ok()); }
}
