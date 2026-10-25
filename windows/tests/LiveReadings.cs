using System;
using System.IO;
using DshWhale;
class LiveReadings {
 static void Main() {
  var settings=Settings.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DSHWhale-Vibe","settings.json"));
  using(var service=new Service(settings,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),3080)) {
   service.Tick(false).GetAwaiter().GetResult();
   foreach(var source in Balances.Layout(settings.Sources,service.Readings)) {
    Reading reading;
    if(service.Readings.TryGetValue(source.Id,out reading))Console.WriteLine("{0}: visible={1}; {2}; {3}",source.Id,source.Visible,reading.Text,reading.Health);
   }
   if(!service.BalancesReachable)Environment.ExitCode=1;
  }
 }
}
