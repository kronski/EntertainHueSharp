using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using System.Threading;
using Microsoft.AspNetCore.Http;

public static class WebServer
{
    static CancellationTokenSource stopper;
    public static async Task Run(CancellationToken token)
    {
        stopper = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Host
            .CreateDefaultBuilder()
            .ConfigureWebHostDefaults(
                builder =>
                {
                    builder.ConfigureKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, 8000);
                    });

                    builder.UseStartup<Startup>();
                })
            .Build()
            .RunAsync(stopper.Token);
    }

    public static void Stop()
    {
        stopper.Cancel();
    }

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages(options =>
            {
                options.Conventions.AddPageRoute("/Calibrate", "");
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }


            app.UseRouting();


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();

                endpoints.MapControllerRoute(name: "image", pattern: "{controller=Image}/{action=Index}");
            });
        }
    }
}