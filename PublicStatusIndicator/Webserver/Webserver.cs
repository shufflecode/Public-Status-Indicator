﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using PublicStatusIndicator.Controller;

namespace PublicStatusIndicator.Webserver
{
    public sealed class HttpServer : IDisposable
    {
        private const uint BUFFER_SIZE = 8192;
        private readonly StreamSocketListener _listener;
        private HttpResponseMessage _response;
        private RouteManager _routemanager;

        public HttpServer(int serverPort = 80)
        {
            _listener = new StreamSocketListener();
            _routemanager = new RouteManager();
            _listener.BindServiceNameAsync(serverPort.ToString());
            _listener.ConnectionReceived += (s, e) => ProcessRequestAsync(e.Socket);
        }

        internal void RegisterController(ApiController controller)
        {
            _routemanager.Register(controller);
            _routemanager.InitRoutes();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private async void ProcessRequestAsync(StreamSocket socket)
        {
            try
            {
                var request = new StringBuilder();
                using (var input = socket.InputStream)
                {
                    var data = new byte[BUFFER_SIZE];
                    var buffer = data.AsBuffer();
                    var dataRead = BUFFER_SIZE;
                    while (dataRead == BUFFER_SIZE)
                    {
                        await input.ReadAsync(buffer, BUFFER_SIZE, InputStreamOptions.Partial);
                        request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                        dataRead = buffer.Length;
                    }
                }
                
                _response = new HttpResponseMessage(HttpStatusCode.Found);
                _response = await _routemanager.InvokeMethod(request.ToString());

                using (var output = socket.OutputStream)
                {
                    await WriteResponseAsync(_response, output);
                }
            }
            catch (Exception ex)
            {
                _response = new HttpResponseMessage(HttpStatusCode.InternalServerError) {Content = new StringContent(ex.Message)};
                using (var output = socket.OutputStream)
                {
                    await WriteResponseAsync(_response, output);
                }
            }
        }

        private async Task WriteResponseAsync(HttpResponseMessage message, IOutputStream os)
        {
            try
            {
                using (var resp = os.AsStreamForWrite())
                {
                    var bodyArray = await message.Content.ReadAsByteArrayAsync();
                    var stream = new MemoryStream(bodyArray);
                    message.Content.Headers.ContentLength = stream.Length;
                    var header = string.Format("HTTP/" + message.Version + " " + (int) message.StatusCode + " " +
                                               message.StatusCode + Environment.NewLine
                                               + "Content-Type: " + message.Content.Headers.ContentType +
                                               Environment.NewLine
                                               + "Content-Length: " + message.Content.Headers.ContentLength +
                                               Environment.NewLine
                                               + "Connection: close\r\n\r\n");
                    var headerArray = Encoding.UTF8.GetBytes(header);
                    await resp.WriteAsync(headerArray, 0, headerArray.Length);
                    await stream.CopyToAsync(resp);
                    await resp.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"error in {nameof(WriteResponseAsync)}: " + ex.Message);
            }
        }


        internal void AddRouteManager(RouteManager manager)
        {
            _routemanager = manager;

        }
    }
}