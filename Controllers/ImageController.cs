using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class ImageController : Controller
{
    public async Task<IActionResult> Index()
    {
        var jpeg = await PiCamera.Instance.GetJpeg();
        if (jpeg is null) return NotFound();

        return File(jpeg, "image/jpeg");
    }
}