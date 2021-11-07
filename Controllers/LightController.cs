using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class LightController : Controller
{
    [HttpPost]
    public async Task<IActionResult> SetLight(byte Segment)
    {
        await HueService.Current.TurnOnSegment(Segment);
        return Ok();
    }
}