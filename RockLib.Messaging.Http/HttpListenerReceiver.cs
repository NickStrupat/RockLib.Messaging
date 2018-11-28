﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace RockLib.Messaging.Http
{
    /// <summary>
    /// An implementation of <see cref="IReceiver"/> that receives http
    /// messages with an <see cref="HttpListener"/>. See
    /// https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener for
    /// moe information.
    /// </summary>
    public class HttpListenerReceiver : Receiver
    {
        private readonly Regex _pathRegex;
        private readonly IReadOnlyCollection<string> _pathTokens;
        private readonly HttpListener _listener;

        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpListenerReceiver"/> class.
        /// </summary>
        /// <param name="name">The name of the receiver.</param>
        /// <param name="prefixes">
        /// The URI prefixes handled by the <see cref="HttpListener"/>. See
        /// https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener for
        /// moe information.
        /// </param>
        /// <param name="path">
        /// The path that requests must match in order to be handled. Any request whose
        /// path does not match this value will receive a 404 Not Found response.
        /// </param>
        /// <param name="acknowledgeStatusCode">
        /// The status code to be returned to the client when a message is acknowledged.
        /// </param>
        /// <param name="acknowledgeStatusDescription">
        /// The status description to be returned to the client when a message is acknowledged.
        /// </param>
        /// <param name="rollbackStatusCode">
        /// The status code to be returned to the client when a message is rolled back.
        /// </param>
        /// <param name="rollbackStatusDescription">
        /// The status description to be returned to the client when a message is rolled back.
        /// </param>
        /// <param name="rejectStatusCode">
        /// The status code to be returned to the client when a message is acknowledged.
        /// </param>
        /// <param name="rejectStatusDescription">
        /// The status description to be returned to the client when a message is rejected.
        /// </param>
        /// <param name="method">
        /// The http method that requests must have in order to be handled. Any request
        /// that does not have this method will receive a 405 Method Not Allowed response.
        /// </param>
        public HttpListenerReceiver(string name, IEnumerable<string> prefixes, string path,
            int acknowledgeStatusCode = 200, string acknowledgeStatusDescription = "OK",
            int rollbackStatusCode = 500, string rollbackStatusDescription = "Internal Server Error",
            int rejectStatusCode = 400, string rejectStatusDescription = "Bad Request",
            string method = "POST")
            : this(name, prefixes, path,
                new DefaultHttpResponseGenerator(acknowledgeStatusCode, acknowledgeStatusDescription, rollbackStatusCode, rollbackStatusDescription, rejectStatusCode, rejectStatusDescription), method)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpListenerReceiver"/> class.
        /// </summary>
        /// <param name="name">The name of the receiver.</param>
        /// <param name="prefixes">
        /// The URI prefixes handled by the <see cref="HttpListener"/>. See
        /// https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener for
        /// moe information.
        /// </param>
        /// <param name="path">
        /// The path that requests must match in order to be handled. Any request whose
        /// path does not match this value will receive a 404 Not Found response.
        /// </param>
        /// <param name="httpResponseGenerator">
        /// An object that determines the http response that is returned to clients,
        /// depending on whether the message is acknowledged, rejected, or rolled back.
        /// </param>
        /// <param name="method">
        /// The http method that requests must have in order to be handled. Any request
        /// that does not have this method will receive a 405 Method Not Allowed response.
        /// </param>
        public HttpListenerReceiver(string name, IEnumerable<string> prefixes, string path,
            IHttpResponseGenerator httpResponseGenerator, string method = "POST")
            : base(name)
        {
            if (prefixes == null)
                throw new ArgumentNullException(nameof(prefixes));

            HttpResponseGenerator = httpResponseGenerator ?? throw new ArgumentNullException(nameof(httpResponseGenerator));
            Method = method ?? throw new ArgumentNullException(nameof(method));

            Path = path?.Trim('/') ?? throw new ArgumentNullException(nameof(path));
            var pathTokens = new List<string>();
            var pathPattern = "^/?" + Regex.Replace(Path ?? "", "{([^}]+)}", m =>
            {
                var token = m.Groups[1].Value;
                pathTokens.Add(token);
                return $"(?<{token}>.*?)";
            }) + "/?$";
            _pathRegex = new Regex(pathPattern, RegexOptions.IgnoreCase);
            _pathTokens = pathTokens;

            _listener = new HttpListener();
            foreach (var prefix in prefixes)
                _listener.Prefixes.Add(prefix);
        }

        /// <summary>
        /// The object that determines the http response that is returned to clients,
        /// depending on whether the message is acknowledged, rejected, or rolled back.
        /// </summary>
        public IHttpResponseGenerator HttpResponseGenerator { get; }

        /// <summary>
        /// Gets the http method that requests must have in order to be handled. Any request
        /// that does not have this method will receive a 405 Method Not Allowed response.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets the path that requests must match in order to be handled. Any request whose
        /// path does not match this value will receive a 404 Not Found response.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc />
        protected override void Start()
        {
            _listener.Start();
            _listener.BeginGetContext(CompleteGetContext, null);
        }

        private void CompleteGetContext(IAsyncResult result)
        {
            if (disposed)
                return;

            var context = _listener.EndGetContext(result);

            _listener.BeginGetContext(CompleteGetContext, null);

            if (!_pathRegex.IsMatch(context.Request.Url.AbsolutePath))
            {
                context.Response.StatusCode = 404;
                context.Response.StatusDescription = "Not Found";
                context.Response.Close();
                return;
            }

            if (context.Request.HttpMethod != Method)
            {
                context.Response.StatusCode = 405;
                context.Response.StatusDescription = "Method Not Allowed";
                context.Response.Close();
                return;
            }

            MessageHandler.OnMessageReceived(this, new HttpListenerReceiverMessage(context, HttpResponseGenerator, _pathRegex, _pathTokens));
        }
        
        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposed)
                return;
            disposed = true;
            _listener.Stop();
            base.Dispose(disposing);
            _listener.Close();
        }
    }
}
