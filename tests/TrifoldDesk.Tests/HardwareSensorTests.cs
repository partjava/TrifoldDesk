using System.Reflection;
using TrifoldDesk.Core;
public static class HardwareSensorTests
{
    public static void Run(Action<bool,string> check)
    {
        var type=typeof(ConfigManager).Assembly.GetType("TrifoldDesk.Core.GpuCounterRules");
        check(type!=null,"GPU counter identity and aggregation rules exist");
        object? Call(string method,params object[] args)=>type!.GetMethod(method)!.Invoke(null,args);
        string a="luid_0x00000001_0x00000002", b="luid_0x00000003_0x00000004";
        check((string?)Call("GetAdapterLuid","pid_12_"+a+"_phys_0_eng_1_engtype_3D")==a,"GPU counter LUID identifies the actual DXGI adapter");
        check(Call("GetAdapterLuid","invalid")==null,"unknown GPU counter layout remains unavailable");
        var values=new Dictionary<string,double>{["pid_1_"+a+"_phys_0_eng_0_engtype_3D"]=20,["pid_2_"+a+"_phys_0_eng_0_engtype_3D"]=35,["pid_3_"+a+"_phys_0_eng_1_engtype_3D"]=10,["pid_4_"+b+"_phys_0_eng_0_engtype_3D"]=95,["pid_5_"+a+"_phys_0_eng_2_engtype_VideoDecode"]=90};
        check((double?)Call("AggregateEngineUtilization",values,a)==55,"selected adapter aggregates processes per 3D engine and ignores other GPU and decode engines");
        check(Call("AggregateEngineUtilization",values,"luid_0x00000000_0x00000000")==null,"missing selected adapter has no fabricated utilization");
        values["pid_1_"+a+"_phys_0_eng_0_engtype_3D"]=double.NaN;values["pid_2_"+a+"_phys_0_eng_0_engtype_3D"]=120;
        check((double?)Call("AggregateEngineUtilization",values,a)==100,"GPU engine percent clamps finite process total and discards NaN");
        var memory=new Dictionary<string,double>{[a+"_phys_0"]=1024,[b+"_phys_0"]=8192};
        check((double?)Call("AggregateDedicatedBytes",memory,a)==1024,"dedicated memory belongs to selected adapter only");
        check(Call("AggregateDedicatedBytes",new Dictionary<string,double>(),a)==null,"missing memory counter stays unavailable");
    }
}
