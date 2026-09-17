using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SurveyApp.Models;
using SurveyApp.Services;
using System.Reflection.Metadata.Ecma335;

namespace SurveyApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SondageController : ControllerBase
    {
        private readonly IServiceSondage _serviceSondage;

        public SondageController(IServiceSondage serviceSondage)
        {
            this._serviceSondage = serviceSondage;
        }

        [HttpGet("{id:int}")]
        public ActionResult<Sondage> GetSondage(int id)
        {
            var temp = this._serviceSondage.GetSondage(id);
            if (temp != null)
                return Ok(temp);
            else
                return NotFound();
        }

        [HttpPost("{idSondage:int}/reponses")]
        public ActionResult<Reponse> ReponseSondage(int idSondage, [FromBody] Reponse reponse)
        {
            var temp = this._serviceSondage.ReponseSondage(idSondage, reponse);
            if (temp == 0)
                return Ok(temp);
            else if (temp == -1)
                return BadRequest();
            else if (temp == -2)
                return Conflict();
            else if (temp == -3)
                return NotFound();
            else if (temp == -4)
                return NotFound();
            else
                return NoContent();
        }

    }
}
