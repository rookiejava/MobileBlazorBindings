// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Platform.Tizen;
using Xamarin.Forms.Platform.Tizen.Native;
using Microsoft.MobileBlazorBindings.WebView.Elements;
using Microsoft.MobileBlazorBindings.WebView.Tizen;
using TWebView = Tizen.WebView.WebView;
using TChromium = Tizen.WebView.Chromium;
using Tizen.WebView;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;

[assembly: ExportRenderer(typeof(WebViewExtended), typeof(WebKitWebViewRenderer))]

namespace Microsoft.MobileBlazorBindings.WebView.Tizen
{
    public class WebKitWebViewRenderer : ViewRenderer<WebViewExtended, WebViewContainer>, IWebViewDelegate
    {
        private bool _isUpdating;
        private WebNavigationEvent _eventState;
        private TWebView NativeWebView => Control.WebView;

        private const string LoadBlazorJSScript = @"
    if (window.location.href.startsWith('http://0.0.0.0/'))
    {
        var blazorScript = document.createElement('script');
        blazorScript.src = 'http://framework/blazor.desktop.js';
        document.body.appendChild(blazorScript);
        (function () {
	        window.onpageshow = function(event) {
		        if (event.persisted) {
			        window.location.reload();
		        }
	        };
        })();
    }";

        private const string InitScriptSource = @"
            window.__receiveMessageCallbacks = [];
            window.__dispatchMessageCallback = function(message) {
	            window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
            };
            window.external = {
	            sendMessage: function(message) {
		            window.BlazorHandler.postMessage(message);
	            },
	            receiveMessage: function(callback) {
		            window.__receiveMessageCallbacks.push(callback);
	            }
            };";

        protected internal IWebViewController ElementController => Element;
        protected internal bool IgnoreSourceChanges { get; set; }
#pragma warning disable CA1056 // Uri properties should not be strings
        protected internal string UrlCanceled { get; set; }
#pragma warning restore CA1056 // Uri properties should not be strings

        public WebKitWebViewRenderer()
        {
            Console.WriteLine($"!!!!!!!!!!! WebKitWebViewRenderer");
            RegisterPropertyHandler(WebViewExtended.SourceProperty, Load);
        }

#pragma warning disable CA1054 // Uri parameters should not be strings
        public void LoadHtml(string html, string baseUrl)
#pragma warning restore CA1054 // Uri parameters should not be strings
        {
            NativeWebView.LoadHtml(html, baseUrl);
        }

#pragma warning disable CA1054 // Uri parameters should not be strings
        public void LoadUrl(string url)
#pragma warning restore CA1054 // Uri parameters should not be strings
        {
            Console.WriteLine($"!!!!!!!!!!!!! - LoadUrl {url}");

            //if (url.StartsWith("app://0.0.0.0"))
            //{
            //    NativeWebView.LoadUrl("file://app/0.0.0.0");
            //    //NativeWebView.LoadUrl("app://0.0.0.0");
            //}
            //else if (!string.IsNullOrEmpty(url))
            {
                NativeWebView.LoadUrl(url);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Control != null)
                {
                    NativeWebView.StopLoading();
                    NativeWebView.LoadStarted -= OnLoadStarted;
                    NativeWebView.LoadFinished -= OnLoadFinished;
                    NativeWebView.LoadError -= OnLoadError;
                }

                if (Element != null)
                {
                    Element.EvalRequested -= OnEvalRequested;
                    Element.EvaluateJavaScriptRequested -= OnEvaluateJavaScriptRequested;
                    Element.GoBackRequested -= OnGoBackRequested;
                    Element.GoForwardRequested -= OnGoForwardRequested;
                    Element.ReloadRequested -= OnReloadRequested;
                    Element.SendMessageFromJSToDotNetRequested -= OnSendMessageFromJSToDotNetRequested;
                }
            }
            base.Dispose(disposing);
        }

        public void PostMessageFromJS(JavaScriptMessage message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

#pragma warning disable CA1307 // Specify StringComparison for clarity
#pragma warning disable CA1309 // Use ordinal string comparison
            if (message.Name.Equals("BlazorHandler"))
#pragma warning restore CA1309 // Use ordinal string comparison
#pragma warning restore CA1307 // Specify StringComparison for clarity
            {

                var body = message.GetBodyAsString().Replace("file://app/", "app://").Replace("file:///", "app://0.0.0.0/").Replace("file://framework/", "framework://");
                Console.WriteLine($"PostMessageFromJS : {message.Name} : {body}");
                Element.HandleWebMessageReceived(body);
            }
        }

        //void (* Ewk_Context_Intercept_Request_Callback) (Ewk_Context* ewk_context, Ewk_Intercept_Request* intercept_request, void* user_data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void Ewk_Context_Intercept_Request_Callback(IntPtr context, IntPtr request, IntPtr userData);

        //EXPORT_API void ewk_context_intercept_request_callback_set(Ewk_Context* ewk_context, Ewk_Context_Intercept_Request_Callback callback, void* user_data);
        [DllImport("libchromium-ewk.so")]
        static extern void ewk_context_intercept_request_callback_set(IntPtr context, Ewk_Context_Intercept_Request_Callback callback, IntPtr userdata);

        //EXPORT_API const char* ewk_intercept_request_url_get(Ewk_Intercept_Request * intercept_request);
        [DllImport("libchromium-ewk.so", EntryPoint = "ewk_intercept_request_url_get")]
        static extern IntPtr ewk_intercept_request_url_get_(IntPtr request);

        static string ewk_intercept_request_url_get(IntPtr request)
        {
            var urlPtr = ewk_intercept_request_url_get_(request);
            return Marshal.PtrToStringAnsi(urlPtr);
        }


        //EXPORT_API Eina_Bool ewk_intercept_request_ignore(Ewk_Intercept_Request* intercept_request);
        [DllImport("libchromium-ewk.so")]
        static extern bool ewk_intercept_request_ignore(IntPtr request);


        //EXPORT_API Eina_Bool ewk_intercept_request_response_set(Ewk_Intercept_Request* intercept_request, const char* headers,const char* body, size_t length);
        [DllImport("libchromium-ewk.so")]
        static extern bool ewk_intercept_request_response_set(IntPtr request, string headers, string body, uint size);

        Ewk_Context_Intercept_Request_Callback _natvieRequestInterceptCallback;

        void EwkRequestInterceptCallback(IntPtr context, IntPtr request, IntPtr userdata)
        {
            var url = ewk_intercept_request_url_get(request);
            var convertedUrl = url.Replace("http://framework/", "framework://");

        

            string scheme = convertedUrl.Split("://")[0];

            Console.WriteLine($"Intercept origin : {url}, Converted {convertedUrl} - scheme : {scheme}");

            if (Element != null && Element.SchemeHandlers.TryGetValue(scheme, out var schemeHandler))
            {

                var uri = new Uri(convertedUrl);
                Console.WriteLine($"uri : Host : {uri.Host} - {uri.AbsolutePath.Substring(1)} - test {uri.Host.Equals("0.0.0.0", StringComparison.Ordinal)}");

                var stream = schemeHandler(convertedUrl, out string contentType);
                if (stream != null)
                {
                    Console.WriteLine($"Intercepted - {convertedUrl}");
                    var header = $"HTTP/1.0 200 OK\r\nContent-Type:{contentType}; charset=utf-8\r\nCache-Control:no-cache, max-age=0, must-revalidate, no-store\r\n\r\n";

                    if (scheme == "framework")
                    {

#pragma warning disable CA2000 // Dispose objects before losing scope
                        var memoryStream = new MemoryStream();
#pragma warning restore CA2000 // Dispose objects before losing scope

                        var buffer = Encoding.UTF8.GetBytes(InitScriptSource);
                        memoryStream.Write(buffer, 0, buffer.Length);
                        stream.CopyTo(memoryStream);
                        stream.Dispose();
                        memoryStream.Position = 0;
                        stream = memoryStream;
                    }

                    var body = new StreamReader(stream).ReadToEnd();
                    ewk_intercept_request_response_set(request, header, body, (uint)body.Length);
                    Console.WriteLine($"Intercepted - {convertedUrl} , header : {header} - content-length:{body.Length}");
                    return;
                }
            }
            ewk_intercept_request_ignore(request);
        }

        protected override void OnElementChanged(ElementChangedEventArgs<WebViewExtended> e)
        {
            if (e is null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            if (Control == null)
            {
                TChromium.Initialize();
                Forms.Context.Terminated += (sender, arg) => TChromium.Shutdown();
                SetNativeControl(new WebViewContainer(Forms.NativeParent));
                NativeWebView.LoadStarted += OnLoadStarted;
                NativeWebView.LoadFinished += OnLoadFinished;
                NativeWebView.LoadError += OnLoadError;
                NativeWebView.AddJavaScriptMessageHandler("BlazorHandler", PostMessageFromJS);

                var context = NativeWebView.GetContext();
                var handleField = context.GetType().GetField("_handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                IntPtr contextHandle = (IntPtr)handleField.GetValue(context);
                Console.WriteLine($"Context Handle {contextHandle}");

                _natvieRequestInterceptCallback = EwkRequestInterceptCallback;
                ewk_context_intercept_request_callback_set(contextHandle, _natvieRequestInterceptCallback, IntPtr.Zero);
                NativeWebView.GetSettings().JavaScriptEnabled = true;
            }

            if (e.OldElement != null)
            {
                e.OldElement.SendMessageFromJSToDotNetRequested -= OnSendMessageFromJSToDotNetRequested;
                e.OldElement.EvalRequested -= OnEvalRequested;
                e.OldElement.GoBackRequested -= OnGoBackRequested;
                e.OldElement.GoForwardRequested -= OnGoForwardRequested;
                e.OldElement.ReloadRequested -= OnReloadRequested;
            }

            if (e.NewElement != null)
            {
                e.NewElement.EvalRequested += OnEvalRequested;
                e.NewElement.EvaluateJavaScriptRequested += OnEvaluateJavaScriptRequested;
                e.NewElement.GoForwardRequested += OnGoForwardRequested;
                e.NewElement.GoBackRequested += OnGoBackRequested;
                e.NewElement.ReloadRequested += OnReloadRequested;
                e.NewElement.SendMessageFromJSToDotNetRequested += OnSendMessageFromJSToDotNetRequested;
                Load();
            }
            base.OnElementChanged(e);
        }

        private void OnSendMessageFromJSToDotNetRequested(object sender, string message)
        {
            Console.WriteLine($"Tizen.WebViewRenderer::OnSendMessageFromJSToDotNetRequested = {message}");
            var messageJSStringLiteral = JavaScriptEncoder.Default.Encode(message);
            Console.WriteLine($"Tizen.WebViewRenderer::OnSendMessageFromJSToDotNetRequested = encoded[{messageJSStringLiteral}]");
            NativeWebView.Eval($"__dispatchMessageCallback(\"{messageJSStringLiteral}\")");
        }

        void OnLoadError(object sender, global::Tizen.WebView.SmartCallbackLoadErrorArgs e)
        {
            string url = e.Url;
            if (!string.IsNullOrEmpty(url))
                SendNavigated(new UrlWebViewSource { Url = url }, _eventState, WebNavigationResult.Failure);
        }

        void OnLoadStarted(object sender, EventArgs e)
        {
            string url = NativeWebView.Url;

            url = url.Replace("file://app/", "app://").Replace("file:///", "app://0.0.0.0/").Replace("file://framework/", "framework://");


            if (!string.IsNullOrEmpty(url))
            {
                var args = new WebNavigatingEventArgs(_eventState, new UrlWebViewSource { Url = url }, url);
                Element.SendNavigating(args);
                Element.HandleNavigationStarting(new Uri(url));

                if (args.Cancel)
                {
                    _eventState = WebNavigationEvent.NewPage;
                }
            }
        }

        void OnLoadFinished(object sender, EventArgs e)
        {
            Console.WriteLine($"OnLoadFinished!!!!");
            string url = NativeWebView.Url;
            if (!string.IsNullOrEmpty(url))
                SendNavigated(new UrlWebViewSource { Url = url }, _eventState, WebNavigationResult.Success);

            NativeWebView.SetFocus(true);
            UpdateCanGoBackForward();

            NativeWebView.Eval(LoadBlazorJSScript);
            //NativeWebView.Eval(InitScriptSource);
            Console.WriteLine($"end eval-");

            Element.HandleNavigationFinished(new Uri(url));

        }

        void Load()
        {
            if (_isUpdating)
                return;

            if (Element.Source != null)
            {
                Element.Source.Load(this);
            }

            UpdateCanGoBackForward();
        }

        void OnEvalRequested(object sender, EvalRequested eventArg)
        {
            NativeWebView.Eval(eventArg.Script);
        }

        Task<string> OnEvaluateJavaScriptRequested(string script)
        {
            NativeWebView.Eval(script);
            return null;
        }

        void OnGoBackRequested(object sender, EventArgs eventArgs)
        {
            if (NativeWebView.CanGoBack())
            {
                _eventState = WebNavigationEvent.Back;
                NativeWebView.GoBack();
            }

            UpdateCanGoBackForward();
        }

        void OnGoForwardRequested(object sender, EventArgs eventArgs)
        {
            if (NativeWebView.CanGoForward())
            {
                _eventState = WebNavigationEvent.Forward;
                NativeWebView.GoForward();
            }

            UpdateCanGoBackForward();
        }

        void OnReloadRequested(object sender, EventArgs eventArgs)
        {
            NativeWebView.Reload();
        }

        void SendNavigated(UrlWebViewSource source, WebNavigationEvent evnt, WebNavigationResult result)
        {
            _isUpdating = true;
            ((IElementController)Element).SetValueFromRenderer(WebViewExtended.SourceProperty, source);
            _isUpdating = false;

            Element.SendNavigated(new WebNavigatedEventArgs(evnt, source, source.Url, result));

            UpdateCanGoBackForward();
            _eventState = WebNavigationEvent.NewPage;
        }

        void UpdateCanGoBackForward()
        {
            ElementController.CanGoBack = NativeWebView.CanGoBack();
            ElementController.CanGoForward = NativeWebView.CanGoForward();
        }
    }
}
