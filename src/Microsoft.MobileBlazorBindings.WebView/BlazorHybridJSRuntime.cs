// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Microsoft.MobileBlazorBindings.WebView
{
    internal class BlazorHybridJSRuntime : JSRuntime
    {
        private IPC _ipc;
        private static readonly Type VoidTaskResultType = typeof(Task).Assembly
            .GetType("System.Threading.Tasks.VoidTaskResult", true);

        public BlazorHybridJSRuntime()
        {
            Console.WriteLine($"Create BlazorHybridJSRuntime");
            JsonSerializerOptions.Converters.Add(new ElementReferenceJsonConverter());
            Console.WriteLine($"Create BlazorHybridJSRuntime - end");
        }

        protected override void BeginInvokeJS(long asyncHandle, string identifier, string argsJson)
        {
            Console.WriteLine($"BeginInvokeJS-- start");
            ThrowIfIpcNotSet();

            _ipc.Send("JS.BeginInvokeJS", asyncHandle, identifier, argsJson);

            Console.WriteLine($"BeginInvokeJS-- end");
        }

        protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
        {
            ThrowIfIpcNotSet();

            // The other params aren't strictly required and are only used for logging
            var resultOrError = invocationResult.Success ? HandlePossibleVoidTaskResult(invocationResult.Result) : invocationResult.Exception.ToString();
            if (resultOrError != null)
            {
                _ipc.Send("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success, resultOrError);
            }
            else
            {
                _ipc.Send("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success);
            }
        }

        private static object HandlePossibleVoidTaskResult(object result)
        {
            // Looks like the TaskGenericsUtil logic in Microsoft.JSInterop doesn't know how to
            // understand System.Threading.Tasks.VoidTaskResult
            return result?.GetType() == VoidTaskResultType ? null : result;
        }

        internal void AttachToIpcChannel(IPC ipc)
        {
            _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        }

        private void ThrowIfIpcNotSet()
        {
            if (_ipc == null)
            {
                throw new InvalidOperationException($"{nameof(AttachToIpcChannel)} must be called before using {nameof(IJSRuntime)}.");
            }
        }
    }
}
