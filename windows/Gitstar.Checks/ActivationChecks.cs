using Gitstar.Desktop;

internal static class ActivationChecks
{
    public static void Run(Action<bool,string> check)
    {
        var scope="checks-"+Guid.NewGuid().ToString("N");
        using var first=new AppActivation(scope);check(first.IsPrimary,"first activation instance owns its isolated mutex");
        using(var second=new AppActivation(scope)){
            check(!second.IsPrimary,"second launch is routed to the existing instance");
            var notification=second.NotifyPrimary();
            check(notification.OwnerProcessId==Environment.ProcessId&&notification.Signaled&&first.Signal.WaitOne(0),"activation hands the owner PID to the launcher and delivers its open request");
            check(!first.Signal.WaitOne(0),"open request signal resets after consumption");
            second.NotifyPrimary();check(first.Signal.WaitOne(0),"repeated launches can wake the same existing instance");
        }
        using var separate=new AppActivation(scope+"-other");check(separate.IsPrimary&&!separate.Signal.WaitOne(0),"different activation scopes do not wake each other");
    }
}
