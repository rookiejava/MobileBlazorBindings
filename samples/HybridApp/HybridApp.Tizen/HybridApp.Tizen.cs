using Microsoft.MobileBlazorBindings.WebView.Elements;
using Microsoft.MobileBlazorBindings.WebView.Tizen;
using System;
using Xamarin.Forms;

[assembly: ExportRenderer(typeof(WebViewExtended), typeof(WebKitWebViewRenderer))]

namespace HybridApp.Tizen
{
    class Program : global::Xamarin.Forms.Platform.Tizen.FormsApplication
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            System.IO.Directory.SetCurrentDirectory(global::Tizen.Applications.Application.Current.DirectoryInfo.Resource);
            LoadApplication(new App());
        }

        static void Main(string[] args)
        {
            var app = new Program();
            BlazorHybridTizen.Init();
            Forms.Init(app);
            app.Run(args);
        }
    }
}
