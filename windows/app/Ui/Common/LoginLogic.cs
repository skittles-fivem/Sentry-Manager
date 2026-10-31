using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SentrySix.Ui
{
    /// <summary>
    /// The behaviour of a sign-in page, shared by the five interfaces; the
    /// words differ per interface and come from each one's login.js.
    /// </summary>
    public sealed class LoginTexts
    {
        public string SetupTitle = "Create your account";
        /// <summary>{0} is where the setup code is printed ("the container log" or "the install window").</summary>
        public string SetupLead;
        public string Submit = "Sign in";
        public string SetupSubmit = "Create account";
        /// <summary>The button's words while waiting (null: unchanged).</summary>
        public string Busy, SetupBusy;
        /// <summary>Empty user name or password (null: the browser's own "required" message).</summary>
        public string Empty = "Please fill out this field.";
        /// <summary>Setup: password shorter than 8 (null: only the browser's minlength check).</summary>
        public string Short = "Please lengthen this text to 8 characters or more.";
        public string Mismatch = "The passwords do not match.";
        /// <summary>{0} is the place of the setup code.</summary>
        public string NoCode;
        public string Unreachable = "Could not reach Sentry Six Manager.";
        /// <summary>"countdown": the error counts down and the button waits (Horizon, Flux); "button": the button counts down (Aurora); "plain": shows the server's words (+ Suffix429).</summary>
        public string Throttle = "plain";
        public string Suffix429;
        /// <summary>Status-specific text when the server sent no error (Horizon: "Sign-in failed (N).").</summary>
        public string FailedStatus;
        public string Fallback;
    }

    public sealed class LoginControls
    {
        public Control User, Password, Confirm, Code;
        public TextBlock Error;
        public Button Submit;
        /// <summary>Sets the button's words (the button may hold an icon too).</summary>
        public Action<string> SetLabel;
        /// <summary>Called while signing in (Horizon's "igniting" card).</summary>
        public Action<bool> Igniting;
    }

    public static class LoginLogic
    {
        public static string LogWhere(LoginModel m)
        {
            return m.WindowsDeployment ? "the install window" : "the container log";
        }

        public static void Wire(LoginModel m, LoginTexts t, LoginControls c)
        {
            bool setup = m.NeedsSetup;
            // (Windows, on the PC itself: no setup code; the form has no code field then)
            bool needCode = setup && m.CodeRequired && c.Code != null;
            Action<string> label = c.SetLabel ?? (s => c.Submit.Content = s);
            label(setup ? t.SetupSubmit : t.Submit);
            DispatcherTimer lockTimer = null;

            Action<int, string, bool> wait = (seconds, text, onButton) =>
            {
                if (lockTimer != null) lockTimer.Stop();
                int left = seconds;
                c.Submit.IsEnabled = false;
                lockTimer = new DispatcherTimer();
                lockTimer.Interval = TimeSpan.FromSeconds(1);
                EventHandler tick = null;
                tick = delegate
                {
                    if (onButton)
                    {
                        c.Error.Text = "Too many attempts. Try again in " + left + " s.";
                        label("Wait " + left + " s");
                    }
                    else c.Error.Text = (Regex.Replace(text, "Try again in \\d+ ?s(econds)?\\.?", "", RegexOptions.IgnoreCase).Trim() + " Try again in " + left + " s.").Trim();
                    if (left-- <= 0)
                    {
                        lockTimer.Stop();
                        c.Submit.IsEnabled = true;
                        c.Error.Text = onButton ? "You can try again now." : "";
                        if (onButton) label(setup ? t.SetupSubmit : t.Submit);
                    }
                };
                lockTimer.Tick += tick;
                tick(null, null);
                lockTimer.Start();
            };

            Func<Task> submit = async () =>
            {
                if (!c.Submit.IsEnabled) return;
                string user = U.Value(c.User), pass = U.Value(c.Password);
                string confirm = c.Confirm != null ? U.Value(c.Confirm) : "", code = c.Code != null ? U.Value(c.Code) : "";
                c.Error.Text = "";
                if (user.Length == 0 || pass.Length == 0)
                {
                    c.Error.Text = t.Empty;
                    (user.Length == 0 ? c.User : c.Password).Focus();
                    return;
                }
                if (setup)
                {
                    if (pass.Length < 8 && t.Short != null) { c.Error.Text = t.Short; return; }
                    if (pass != confirm) { c.Error.Text = t.Mismatch; return; }
                    if (needCode && code.Trim().Length == 0) { c.Error.Text = t.NoCode != null ? string.Format(t.NoCode, LogWhere(m)) : t.Empty; return; }
                }
                c.Submit.IsEnabled = false;
                string before = c.Submit.Content as string;
                string busy = setup ? t.SetupBusy : t.Busy;
                if (busy != null) label(busy);
                if (c.Igniting != null) c.Igniting(true);
                LoginResult r;
                try { r = await m.Submit(user, pass, setup ? (needCode ? code : "") : null); }
                catch (Exception ex) { r = new LoginResult { Ok = false, Status = 0, Error = ex.Message }; }
                c.Submit.IsEnabled = true;
                if (busy != null) label(setup ? t.SetupSubmit : t.Submit);
                if (r.Ok) return;
                if (c.Igniting != null) c.Igniting(false);
                string msg = r.Error;
                if (string.IsNullOrEmpty(msg))
                {
                    if (r.Status == 0) msg = t.Unreachable;
                    else if (t.FailedStatus != null) msg = string.Format(t.FailedStatus, r.Status);
                    else msg = t.Fallback ?? t.Unreachable;
                }
                if (r.Status == 429)
                {
                    if (t.Throttle == "countdown" || t.Throttle == "button")
                    {
                        Match mm = Regex.Match(msg, "(\\d+)\\s*s");
                        wait(mm.Success ? int.Parse(mm.Groups[1].Value) : 30, msg, t.Throttle == "button");
                        return;
                    }
                    if (t.Suffix429 != null) msg = msg + t.Suffix429;
                }
                c.Error.Text = msg;
                PasswordBox pb = c.Password as PasswordBox;
                if (pb != null) { pb.Focus(); pb.SelectAll(); }
            };

            c.Submit.Click += async delegate { await submit(); };
            KeyEventHandler enter = async delegate(object s, KeyEventArgs a)
            {
                if (a.Key != Key.Enter) return;
                a.Handled = true;
                await submit();
            };
            foreach (Control f in new[] { c.User, c.Password, c.Confirm, c.Code })
                if (f != null) f.KeyDown += enter;
            c.User.Loaded += delegate
            {
                // the web pages focus the first empty field
                if (U.Value(c.User).Length == 0) c.User.Focus();
                else c.Password.Focus();
            };
        }

        /// <summary>The fingerprint as the web shows it: a line may break after each colon.</summary>
        public static string Breakable(string fp)
        {
            return (fp ?? "").Replace(":", ":​");
        }

        /// <summary>The service log the Windows wording names in place of "docker logs sentry-six-manager".</summary>
        public const string ServiceLog = "%ProgramData%\\SentrySixManager\\data\\logs\\service.log";
        public const string DockerLogs = "docker logs sentry-six-manager";
    }
}
