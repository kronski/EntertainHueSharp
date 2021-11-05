using System.Net;
using System.Text;
using System.Threading.Tasks;

public static class Calibrate
{
    public static HttpListener listener;
    public static string url = "http://localhost:8000/";
    public static int pageViews = 0;
    public static int requestCount = 0;
    public static string pageData = @"
        <!DOCTYPE>
        <html>
          <head>
            <title>HttpListener Example</title>
          </head>
          <body>
            <p>Page Views: {0}</p>
            <form method=""post"" action=""shutdown"">
              <input type=""submit"" value=""Shutdown"" {1}>
            </form>
          </body>
        </html>";
    public static async Task Run()
    {
        listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        await ConsoleEx.Write("Listening for connections on ", url);
        await HandleIncomingConnections();

        listener.Close();
    }

    public static async Task HandleIncomingConnections()
    {
        bool runServer = true;

        // While a user hasn't visited the `shutdown` url, keep on handling requests
        while (runServer)
        {
            // Will wait here until we hear from a connection
            HttpListenerContext ctx = await listener.GetContextAsync();

            // Peel out the requests and response objects
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse resp = ctx.Response;

            // Print out some info about the request
            await ConsoleEx.Verbose("Request #: {0}", (++requestCount).ToString());
            await ConsoleEx.Verbose(req.Url.ToString());
            await ConsoleEx.Verbose(req.HttpMethod);
            await ConsoleEx.Verbose(req.UserHostName);
            await ConsoleEx.Verbose(req.UserAgent);
            await ConsoleEx.Verbose();

            // If `shutdown` url requested w/ POST, then shutdown the server after serving the page
            if ((req.HttpMethod == "POST") && (req.Url.AbsolutePath == "/shutdown"))
            {
                await ConsoleEx.Verbose("Shutdown requested");
                runServer = false;
            }

            // Make sure we don't increment the page views counter if `favicon.ico` is requested
            if (req.Url.AbsolutePath != "/favicon.ico")
                pageViews += 1;

            // Write the response info
            string disableSubmit = !runServer ? "disabled" : "";
            byte[] data = Encoding.UTF8.GetBytes(string.Format(pageData, pageViews, disableSubmit));
            resp.ContentType = "text/html";
            resp.ContentEncoding = Encoding.UTF8;
            resp.ContentLength64 = data.LongLength;

            // Write out to the response stream (asynchronously), then close it
            await resp.OutputStream.WriteAsync(data, 0, data.Length);
            resp.Close();
        }
    }
}