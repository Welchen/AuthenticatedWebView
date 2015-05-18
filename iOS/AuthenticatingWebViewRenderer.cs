﻿using System;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using Security;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;
using AuthenticatingWebViewTest;
using AuthenticatingWebViewTest.iOS;

[assembly: ExportRenderer(typeof(AuthenticatingWebView), typeof(AuthenticatingWebViewRenderer))]

namespace AuthenticatingWebViewTest.iOS
{
    public class AuthenticatingWebViewRenderer : WebViewRenderer
    {
        public new AuthenticatingWebView Element { get { return (AuthenticatingWebView)base.Element; } }

        protected override void OnElementChanged (VisualElementChangedEventArgs e)
        {
            base.OnElementChanged (e);

            if (!(Delegate is AuthenticatingWebViewDelegate))
            {
                var originalDelegate = (NSObject)Delegate;

                // We are deliberately overwriting this delegate.
                // If we don't null it out first then we would get an exception.
                Delegate = null;
                Delegate = new AuthenticatingWebViewDelegate(this, originalDelegate);
            }
        }

        private class AuthenticatingWebViewDelegate : UIWebViewDelegate, INSUrlConnectionDelegate
        {
            private readonly AuthenticatingWebViewRenderer _renderer;
            private readonly NSObject _originalDelegate;
            private NSUrlRequest _request;

            public AuthenticatingWebViewDelegate(AuthenticatingWebViewRenderer renderer, NSObject originalDelegate)
            {
                _renderer = renderer;
                _originalDelegate = originalDelegate;
            }

            public override void LoadFailed (UIWebView webView, NSError error)
            {
                ForwardDelegateMethod("webView:didFailLoadWithError:", webView, error);
            }

            public override void LoadingFinished (UIWebView webView)
            {
                ForwardDelegateMethod("webViewDidFinishLoad:", webView);
            }

            public override void LoadStarted (UIWebView webView)
            {
                ForwardDelegateMethod("webViewDidStartLoad:", webView);
            }

            public override bool ShouldStartLoad (UIWebView webView, NSUrlRequest request, UIWebViewNavigationType navigationType)
            {
                if (_request != null)
                {
                    _request = null;
                    return true;
                }

                bool originalResult = ForwardDelegatePredicate("webView:shouldStartLoadWithRequest:navigationType:", webView, request, (int)navigationType, defaultResult: true);

                if (_renderer.Element.ShouldTrustCertificate != null)
                {
                    if (originalResult)
                    {
                        _request = request;
                         new NSUrlConnection(request, this, startImmediately: true);
                    }
                    return false;
                }

                return originalResult;
            }

            [Export ("connection:willSendRequestForAuthenticationChallenge:")]
            private void WillSendRequestForAuthenticationChallenge (NSUrlConnection connection, NSUrlAuthenticationChallenge challenge)
            {
                if (challenge.ProtectionSpace.AuthenticationMethod == NSUrlProtectionSpace.AuthenticationMethodServerTrust)
                {
                    var trust = challenge.ProtectionSpace.ServerSecTrust;
                    bool trustedCert = false;
                    for (int i = 0; i != trust.Count; ++i)
                    {
                        var cert = new Certificate(challenge.ProtectionSpace.Host, trust[i].ToX509Certificate2());
                        if (_renderer.Element.ShouldTrustCertificate(cert))
                        {
                            challenge.Sender.UseCredential(new NSUrlCredential(trust), challenge);
                            trustedCert = true;
                            break;
                        }
                    }
                    if (!trustedCert)
                    {
                        Console.WriteLine("Rejecting request");
                        challenge.Sender.CancelAuthenticationChallenge(challenge);

                        // TODO: Send failed repsonse.
                        // This is hard because Xamarin made SendNavigated internal for some idiotic reason.
                        // We'll have to use reflection. *sigh*

                        return;
                    }
                }
                challenge.Sender.PerformDefaultHandling(challenge);
            }

            [Export ("connection:didReceiveResponse:")]
            private void ReceivedResponse (NSUrlConnection connection, NSUrlResponse response)
            {
                connection.Cancel();
                _renderer.LoadRequest(_request);
            }

            #region Ugly wrapper stuff

            [DllImport ("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
            private static extern bool void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

            [DllImport ("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
            private static extern bool void_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

            [DllImport ("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
            private static extern bool bool_objc_msgSend_IntPtr_IntPtr_int(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, int arg3);

            private void ForwardDelegateMethod(string selName, NSObject arg1)
            {
                var sel = new Selector(selName);
                if (_originalDelegate.RespondsToSelector(sel))
                {
                    void_objc_msgSend_IntPtr(
                        _originalDelegate.Handle,
                        sel.Handle,
                        arg1 != null ? arg1.Handle : IntPtr.Zero);
                }
            }

            private void ForwardDelegateMethod(string selName, NSObject arg1, NSObject arg2)
            {
                var sel = new Selector(selName);
                if (_originalDelegate.RespondsToSelector(sel))
                {
                    void_objc_msgSend_IntPtr_IntPtr(
                        _originalDelegate.Handle,
                        sel.Handle,
                        arg1 != null ? arg1.Handle : IntPtr.Zero,
                        arg2 != null ? arg2.Handle : IntPtr.Zero);
                }
            }

            private bool ForwardDelegatePredicate(string selName, NSObject arg1, NSObject arg2, int arg3, bool defaultResult)
            {
                var sel = new Selector(selName);
                if (_originalDelegate.RespondsToSelector(sel))
                {
                    return bool_objc_msgSend_IntPtr_IntPtr_int(
                        _originalDelegate.Handle,
                        sel.Handle,
                        arg1 != null ? arg1.Handle : IntPtr.Zero,
                        arg2 != null ? arg2.Handle : IntPtr.Zero,
                        arg3);
                }

                return defaultResult;
            }

            #endregion
        }
    }
}
