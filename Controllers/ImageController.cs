using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class ImageController : Controller
{
    public async Task<IActionResult> Index()
    {
        Stream jpeg = null;
        try
        {
            jpeg = await PiCamera.Instance.GetJpeg();
        }
        catch
        {

        }
        if (jpeg is null) return NotFound();

        return File(jpeg, "image/jpeg");
    }
}