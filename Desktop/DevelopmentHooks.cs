#if AIRBRIDGE_DEVELOPMENT
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  readonly AudioExperiment audioExperiment=new AudioExperiment();
  partial void TickDevelopment(){if(AudioExperiment.Enabled&&!routes.Busy)audioExperiment.Tick(routes.Engine,settings.Root);TickOutputComparison();}
  partial void RunDevelopmentSelfTest(){Run(SelfTest);}
 }
}
#endif
