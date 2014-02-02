using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WebServer
{
    /* Response type enumeration */
    enum ResponseType
    {
        NOT_FOUND,
        NOT_ALLOWED,
        OK,
        ERROR
    }

    /*
     * This is a VERY basic web server that will listen for 
     * HTTP GET requests and serve back a requested resource
     * from some designated directory on the server maching. 
     * 
     * The server will support basic dynamic web pages using
     * a subset of C# in both script and web template form.
     */
    class WebServer
    {
        private readonly string _webRoot;
        private readonly string _defaultDoc;
        private IScriptProcessor _scriptProcessor;

        static void Main(string[] args)
        {
            /* if the user does not provide a port number, default to 8080 */
            int port = 8080;
            try
            {
                port = Convert.ToInt16(args[0]);
            }
            catch (Exception) { }

            /* if the user does not provide a web root, default to /wwwroot */
            string webRoot = args.Count() > 1 ? args[1] : @"C:\wwwroot";

            /* if the user does not provide a default document, use index.html */
            string defaultDoc = args.Count() > 2 ? args[2] : "index.html";

            /* create an instance of the web server and start listening for requests */
            new WebServer(port, webRoot, defaultDoc);
        }

        public WebServer(int port, string root, string defaultDoc)
        {
            /* this script processor instance will be used to process files of type 
             * csscript */
            _scriptProcessor = new CscriptProcessor();

            /*TODO: add another instance of a IScriptProcessor to handle files of
             * type csweb */

            /* set the root for the server */
            _webRoot = root;

            /* set the default doc for the server */
            _defaultDoc = defaultDoc;

            /* create a TcpListener to listen for netweork requests on the provided
             * port number at the lookedup host address and start listening */
            TcpListener listener = new TcpListener(
                Dns.GetHostAddresses("localhost")[0], port);
            listener.Start();
            Console.WriteLine("Web server listening on port {0}", port);

            /* main body of the web server, this will listen for requests, 
             * open a socket with the client when a request is received 
             * and spawn a process thread for accepting the request and 
             * return to listen for the next request */
            while (true)
            {
                Socket soc = listener.AcceptSocket();
                new Task(delegate()
                {
                    AcceptRequest(soc);
                }).Start();
            }
        }

        public void AcceptRequest(Socket socket)
        {
            if (socket.Connected)
            {
                /* create a buffer for the header and read in data from the
                 * client 1024 bytes at a time, ending only after a full
                 * header has been received */
                StringBuilder headerBuf = new StringBuilder();

                byte[] content = new byte[1024];
                while (true)
                {
                    int i = socket.Receive(content, 1024, 0);
                    if (i == 0) { return; }

                    headerBuf.Append(Encoding.ASCII.GetString(content));

                    /* if the data read thus far contains the header termination
                     * string, then we have seen the entire header and can get 
                     * on with it */
                    if (headerBuf.ToString().Contains("\r\n\r\n"))
                    {
                        break;
                    }
                }
                string header = headerBuf.ToString();

                /* generally this kind of debug statement would not be left 
                 * in the code as it adds unneccessary overhead to processing
                 * the request */
                Console.WriteLine(header);

                /* check to see if the request is a GET request and return an
                 * HTTP 405 (not allowed) if it is not as our simple web server
                 * will only support GET requests */
                if (header.Substring(0, 3) != "GET")
                {
                    _SendResponse(socket, new byte[0], null, ResponseType.NOT_ALLOWED);
                }

                /* pull out the path being requested by looking between the GET and HTTP
                 * values in the first line of the header
                 * 
                 * GET /somepath/to/some/thing HTTP/1.1
                 */
                string resource = header.Substring(4, header.IndexOf("HTTP") - 4).Trim();

                /* If a directory is being requested, check to see if the default file exists.
                 * Otherwise, return a 404 as expected.
                 */

                string directoryPath = string.Format("{0}{1}", _webRoot, resource);
                if (Directory.Exists(directoryPath))
                {
                    resource = string.Format("{0}\\{1}", resource, _defaultDoc);
                }

                /* if an actual resource was requested, append the webroot to it to transform 
                    * the path to a system local path and parse the full path to separate the path
                    * from the request variables */
                resource = string.Format("{0}{1}", _webRoot, resource.Replace("/", @"\"));
                string[] parts = resource.Split('?');
                resource = parts[0]; // the resource is the first half of the path

                /* the request variables are the second part of the path and these are loaded
                    * into an IDictionary instance to be used later */
                Dictionary<string, string> requestParameters = parts.Count() > 1 ? parts[1].Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('='))
                        .ToDictionary(split => split[0], split => split[1]) : new Dictionary<string, string>();

                /* if the path is to a file that exists under the webroot directory, 
                    * create an HTTP response with that file in the response body */
                if (File.Exists(resource))
                {
                    _ProcessBody(socket, resource, requestParameters);
                }
                else
                {
                    /* otherwise generate a Not Found (404) response */ 
                    _SendResponse(socket, new byte[0], null, ResponseType.NOT_FOUND);
                }
            }

            
            socket.Close(); // always make sure to close network and file handles!!
        }

        /* A method for processing the requested file to return to the client in a response body.
         * The method takes a valid path and a dictionary of request parameters*/
        private void _ProcessBody(Socket socket, string path, Dictionary<string, string> requestParameters)
        {
            /* Using the file extension, determine the mime type of the file. Note 
             * this simple server only supports a VERY limited set of types */
            String type = Path.GetExtension(path);

            switch (type)
            {
                case ".gif":
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".tiff":
                    type = string.Format("image/{0}", type.Substring(1));
                    break;
                case ".html":
                case ".htm":
                    type = "text/html; charset=utf8";
                    break;

                /* this is a special case as the requested file needs to be executed and the 
                 * result of the execution returned as the response body rather than the 
                 * file itself */
                case ".csscript": 
                    {
                        _GenerateScriptResult(socket, path, requestParameters);
                        return;
                    }

                /* TODO: add another handler for processing web template files
                 * case ".csweb": 
                 */
                default:
                    type = "application/octet-stream";
                    break;
            }

            /* read the contents of the file into a memory stream */
            using (FileStream file = File.OpenRead(path))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    byte[] b = new byte[1024];
                    int len = 0;
                    while ((len = file.Read(b, 0, 1024)) > 0)
                    {
                        stream.Write(b, 0, len);
                    }

                    /* send an HTTP OK (202) response with the contents of the 
                     * file back to the client */
                    _SendResponse(socket, stream.ToArray(), type, ResponseType.OK);
                }
            }
        }

        /* This method will create an HTTP response and send it through the argument socket back to the
         * client. The call specifies an optional response body, mime type (where a body is provided)
         * and the type of response. */
        private void _SendResponse(Socket socket, byte[] body, string mimeType, ResponseType type)
        {
            StringBuilder response = new StringBuilder();

            /* convert the type into the appropriate response header value */
            switch (type)
            {
                case ResponseType.NOT_ALLOWED:
                    response.Append("HTTP/1.1 405 Method Not Allowed\r\n");
                    break;
                case ResponseType.ERROR:
                    response.Append("HTTP/1.1 500 Error\r\n");
                    break;
                case ResponseType.NOT_FOUND:
                    response.Append("HTTP/1.1 404 Not Found\r\n");
                    break;
                case ResponseType.OK:
                    response.Append("HTTP/1.1 200 OK\r\n");
                    break;
            }

            /* if a body is being sent, set the mime type and body size in the header */
            if (body.Length > 0)
            {
                response.Append(string.Format("Content-Type: {0}\r\n", mimeType));
                response.Append(string.Format("Content-Length: {0}\r\n", body.Count()));
            }

            /* Add in the timestamp for the request, the server type, and instruct the 
             * client that we will be closing the socket once this is all sent off. 
             * Again, this is a very simple server so we don't implement much of the 
             * HTTP spec */
            response.Append(string.Format("Date: {0}\r\n", DateTime.Now.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'")));
            response.Append("Server: Simple Web Server");
            response.Append("Connection: Close\r\n\n");

            /* encode and send the header to the client */
            socket.Send(Encoding.ASCII.GetBytes(response.ToString()));

            /* if there is a body, send that along before closing the socket */
            if (body.Length > 0)
            {
                socket.Send(body);
            }
        }

        /* This method will process a script file and send the results as the 
         * body of the response */
        private void _GenerateScriptResult(Socket socket, string path, Dictionary<string, string> requestParameters)
        {
            /* get a script result from the scrupt processor using the request parameter dictionary */
            ScriptResult result = _scriptProcessor.ProcessScript(path, requestParameters);

            /* if the result was an error, send an HTTP Error (500) along wiht a summary of 
             * what went wrong as the body */
            if (result.Error)
            {
                _SendResponse(socket, Encoding.ASCII.GetBytes(result.Result), "text/html; charset=utf8", ResponseType.ERROR);
            }
            else
            {
                /* send a response with the results of the script evaluation */
                _SendResponse(socket, Encoding.ASCII.GetBytes(result.Result), "text/html; charset=utf8", ResponseType.OK);
            }
        }
    }
}
