Console.OutputEncoding=new System.Text.UTF8Encoding(false);
Console.InputEncoding=new System.Text.UTF8Encoding(false);
Console.ReadLine();
if(args.Contains("eof")){Native.CloseHandle(Native.GetStdHandle(-11));Thread.Sleep(20000);return;}
if(args.Contains("invalid")){Console.WriteLine(new string('x',17000));Thread.Sleep(20000);return;}
Console.WriteLine("{\"title\":\"插件探针\",\"lines\":[\"真实进程输出\"]}");
Console.Out.Flush();Thread.Sleep(20000);
static class Native
{
 [System.Runtime.InteropServices.DllImport("kernel32.dll")]public static extern IntPtr GetStdHandle(int handle);
 [System.Runtime.InteropServices.DllImport("kernel32.dll")]public static extern bool CloseHandle(IntPtr handle);
}
