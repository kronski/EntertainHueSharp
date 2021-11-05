using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;

public class CalibrateModel : PageModel
{
    public int CurrentLight { get; private set; }

    public void OnGet()
    {
        this.CurrentLight = 1;
    }

    public void OnPost(int? Stop, int? Next)
    {
        this.CurrentLight = (Next ?? 0) + 1;
        if (Stop == 1)
            WebServer.Stop();
    }
}
