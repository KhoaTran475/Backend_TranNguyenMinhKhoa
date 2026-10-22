using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/students"), Authorize(Roles = Roles.Admin)]
public class StudentsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet][ProducesResponseType(typeof(ResponseEnvelope<PageResult<Student>>), 200)] public async Task<IActionResult> List([FromQuery] ListQuery p) { var q = db.Students.AsNoTracking(); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.StudentCode.Contains(p.Search) || x.FullName.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderBy(x => x.Id).PageAsync(p))); }
    [HttpGet("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Student>), 200)] public async Task<IActionResult> Get(int id) => Ok(ApiResponse.Ok(await db.Students.AsNoTracking().Where(x => x.Id == id).Required()));
    [HttpPost][ProducesResponseType(typeof(ResponseEnvelope<Student>), 201)] public async Task<IActionResult> Create(StudentRequest r) { var entity = new Student { StudentCode = r.StudentCode.Trim(), FullName = r.FullName.Trim(), Email = r.Email.Trim() }; db.Students.Add(entity); await db.SaveChangesAsync(); return CreatedAtAction(nameof(Get), new { id = entity.Id }, ApiResponse.Ok(entity)); }
    [HttpPut("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<Student>), 200)] public async Task<IActionResult> Update(int id, StudentRequest r) { var entity = await db.Students.Where(x => x.Id == id).Required(); ApiSupport.Require(entity.UserId == null, "Hồ sơ đã liên kết tài khoản; không sửa thông tin định danh.", 409); entity.StudentCode = r.StudentCode.Trim(); entity.FullName = r.FullName.Trim(); entity.Email = r.Email.Trim(); await db.SaveChangesAsync(); return Ok(ApiResponse.Ok(entity)); }
    [HttpDelete("{id:int}")][ProducesResponseType(typeof(ResponseEnvelope<object>), 200)] public async Task<IActionResult> Delete(int id) { var entity = await db.Students.Where(x => x.Id == id).Required(); ApiSupport.Require(!(await db.Enrollments.AnyAsync(x => x.StudentId == id) || await db.Attendances.AnyAsync(x => x.StudentId == id) || entity.UserId != null), "Dữ liệu đã được sử dụng; không thể xóa.", 409); db.Students.Remove(entity); await db.SaveChangesAsync(); return Ok(ApiResponse.Ok()); }
}
