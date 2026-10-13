namespace Gitstar.Core;

public static class OperationRules
{
    public static bool CanCancel(Operation op)=>op.State!="完成"&&!op.State.StartsWith("已取消",StringComparison.Ordinal);
    public static void CancelStopped(Operation op)
    {
        if(!CanCancel(op))return;op.State=op.RemoteId!=null||op.State.Contains("待核实")?"已取消 · 远程结果待核实":"已取消";op.Updated=DateTimeOffset.Now;
    }
}
