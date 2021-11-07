using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;
public class CalibrateModel : PageModel
{
    public byte CurrentLight { get; private set; }

    public async Task OnGet()
    {
        CurrentLight = 0;
        await HueService.Current.TurnOnSegment(CurrentLight);
    }

    public async Task OnPost(byte? Stop, byte? Next, byte? Reset)
    {
        if (Reset != null)
        {
            CurrentLight = 0;
            await HueService.Current.TurnOnSegment(CurrentLight);
        }
        else if (Next != null)
        {
            CurrentLight = (byte)(Next.Value + 1);
            await HueService.Current.TurnOnSegment(CurrentLight);
        }
        else
        if (Stop == 1)
            WebServer.Stop();
    }
}
