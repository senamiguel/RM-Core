using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Threading;

namespace RM.HostCheck
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 3 ||
                !int.TryParse(args[1], out int port) ||
                !int.TryParse(args[2], out int timeoutMs))
            {
                Console.Error.WriteLine("USAGE: RM.HostCheck.exe <binDir> <hostClientPort> <timeoutMs>");
                return 3;
            }

            string binDir = args[0];

            try
            {
                foreach (var dllPath in Directory.GetFiles(binDir, "RM.Lib*.dll"))
                    Assembly.LoadFrom(dllPath);

                if (ChannelServices.RegisteredChannels.Length == 0)
                {
                    var provider = new BinaryServerFormatterSinkProvider();
                    provider.TypeFilterLevel = TypeFilterLevel.Full;
                    var props = new Hashtable();
                    props["port"] = 0;
                    props["name"] = "HostCheckChannel";
                    var channel = new TcpChannel(props, null, provider);
                    ChannelServices.RegisterChannel(channel, false);
                }

                Assembly rmLib = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("RM.Lib"))
                    {
                        rmLib = asm;
                        break;
                    }
                }

                if (rmLib == null)
                {
                    Console.Error.WriteLine("RM.Lib assembly not loaded.");
                    return 2;
                }

                Type rmsBrokerType = rmLib.GetType("RM.Lib.RMSBroker");
                if (rmsBrokerType == null)
                {
                    rmsBrokerType = Type.GetType("RM.Lib.RMSBroker");
                    if (rmsBrokerType == null)
                    {
                        Console.Error.WriteLine("RMSBroker type not found.");
                        return 2;
                    }
                }

                object paramsObj = rmsBrokerType.GetProperty("Params", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                Type paramsType = paramsObj.GetType();

                paramsType.GetProperty("HostClientPort").SetValue(paramsObj, port);
                paramsType.GetProperty("UseHostClient").SetValue(paramsObj, true);

                string serviceId = paramsType.GetProperty("HostClientSecurityServerId").GetValue(paramsObj) as string;
                if (string.IsNullOrEmpty(serviceId))
                {
                    Console.Error.WriteLine("HostClientSecurityServerId not set.");
                    return 2;
                }

                Type serverInterface = rmLib.GetType("RM.Lib.Client.IRMSClientSecurityServer");
                if (serverInterface == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        serverInterface = asm.GetType("RM.Lib.Client.IRMSClientSecurityServer");
                        if (serverInterface != null) break;
                    }
                }

                if (serverInterface == null)
                {
                    Console.Error.WriteLine("IRMSClientSecurityServer type not found in any loaded assembly.");
                    return 2;
                }

                string url = $"tcp://localhost:{port}/{serviceId}";
                object securityServer = Activator.GetObject(serverInterface, url);

                int elapsed = 0;
                while (elapsed < timeoutMs)
                {
                    try
                    {
                        object principal = serverInterface.InvokeMember("GetSecurityContext", BindingFlags.InvokeMethod, null, securityServer, null);
                        if (principal != null)
                        {
                            object identity = principal.GetType().InvokeMember("Identity", BindingFlags.GetProperty, null, principal, null);
                            if (identity != null)
                            {
                                bool isAuth = (bool)identity.GetType().InvokeMember("IsAuthenticated", BindingFlags.GetProperty, null, identity, null);
                                if (isAuth)
                                    return 0;
                            }
                        }
                    }
                    catch
                    {
                    }

                    Thread.Sleep(500);
                    elapsed += 500;
                }

                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }
    }
}
