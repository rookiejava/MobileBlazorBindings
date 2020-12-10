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

[assembly: ExportRenderer(typeof(WebViewExtended), typeof(WebKitWebViewRenderer))]

namespace Microsoft.MobileBlazorBindings.WebView.Tizen
{
    public class WebKitWebViewRenderer : ViewRenderer<WebViewExtended, WebViewContainer>, IWebViewDelegate
    {
        private bool _isUpdating;
        private WebNavigationEvent _eventState;
        private TWebView NativeWebView => Control.WebView;

        private const string LoadBlazorJSScript =
            "window.onload = (function blazorInitScript() {" +
            "    var blazorScript = document.createElement('script');" +
            "    blazorScript.src = 'framework://blazor.desktop.js';" +
            "    document.body.appendChild(blazorScript);" +
            "});";

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
            if (!string.IsNullOrEmpty(url))
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
                Element.HandleWebMessageReceived(message.GetBodyAsString());
            }
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
            var messageJSStringLiteral = JavaScriptEncoder.Default.Encode(message);
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
            if (!string.IsNullOrEmpty(url))
            {
                var args = new WebNavigatingEventArgs(_eventState, new UrlWebViewSource { Url = url }, url);
                Element.SendNavigating(args);

                if (args.Cancel)
                {
                    _eventState = WebNavigationEvent.NewPage;
                }
            }
        }

        void OnLoadFinished(object sender, EventArgs e)
        {
            string url = NativeWebView.Url;
            if (!string.IsNullOrEmpty(url))
                SendNavigated(new UrlWebViewSource { Url = url }, _eventState, WebNavigationResult.Success);

            NativeWebView.SetFocus(true);
            UpdateCanGoBackForward();

            NativeWebView.Eval(LoadBlazorJSScript);
            NativeWebView.Eval(InitScriptSource);
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
