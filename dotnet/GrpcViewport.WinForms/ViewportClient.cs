using System;
using System.Net.Http;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace GrpcViewport.WinForms
{
    internal static class ViewportClient
    {
        /// <summary>The host's HTTP/1.1 port: gRPC-Web + the page.</summary>
        public static readonly Uri BaseUri = new Uri("http://127.0.0.1:8080");

        /// <summary>Create ONE channel for the app lifetime and reuse it.</summary>
        public static GrpcChannel CreateChannel()
        {
            var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler())
            {
                // .NET Framework's HttpClient only speaks HTTP/1.x.
                HttpVersion = new Version(1, 1),
            };

            return GrpcChannel.ForAddress(BaseUri, new GrpcChannelOptions
            {
                HttpHandler = handler,
                MaxReceiveMessageSize = null, // no 4 MB receive limit
                MaxSendMessageSize = null,
            });
        }
    }
}