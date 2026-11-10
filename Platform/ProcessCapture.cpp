#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <wrl.h>
#include <vector>
using namespace Microsoft::WRL;
typedef void (__stdcall *Samples)(const BYTE*,int);
typedef void (__stdcall *Ready)(HRESULT);
class Activation final : public RuntimeClass<RuntimeClassFlags<ClassicCom>,FtmBase,IActivateAudioInterfaceCompletionHandler> {
public:
 AUDIOCLIENT_ACTIVATION_PARAMS parameters={};PROPVARIANT value={};
 HANDLE done=CreateEvent(nullptr,TRUE,FALSE,nullptr);HRESULT result=E_PENDING;ComPtr<IAudioClient> client;
 ~Activation(){CloseHandle(done);}
 HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* op) override {
  ComPtr<IUnknown> object;HRESULT activation=E_FAIL;result=op->GetActivateResult(&activation,&object);
  if(SUCCEEDED(result))result=activation;if(SUCCEEDED(result))result=object.As(&client);SetEvent(done);return S_OK;
 }
};
extern "C" __declspec(dllexport) HRESULT __stdcall CaptureProcess(DWORD pid,BOOL exclude,volatile LONG* stop,Samples samples,Ready ready){
 HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(init)){ready(init);return init;}
 HRESULT hr=S_OK;bool notified=false;HANDLE signal=nullptr;ComPtr<IAudioClient> audio;
 auto run=[&]()->HRESULT {
  auto callback=Make<Activation>();if(!callback||!callback->done)return E_OUTOFMEMORY;
  auto& parameters=callback->parameters;parameters.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
  parameters.ProcessLoopbackParams.TargetProcessId=pid;parameters.ProcessLoopbackParams.ProcessLoopbackMode=exclude?PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE:PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
  auto& value=callback->value;value.vt=VT_BLOB;value.blob.cbSize=sizeof(parameters);value.blob.pBlobData=reinterpret_cast<BYTE*>(&parameters);
  ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
  HRESULT status=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&value,callback.Get(),&operation);
  if(FAILED(status))return status;
  // Keep the activation payload alive until the completion callback has consumed it.
  for(int n=0;;n++){if(WaitForSingleObject(callback->done,100)==WAIT_OBJECT_0)break;if(n>=149)return HRESULT_FROM_WIN32(WAIT_TIMEOUT);}
  if(FAILED(callback->result))return callback->result;audio=callback->client;
  WAVEFORMATEX format={WAVE_FORMAT_PCM,2,44100,176400,4,16,0};
  status=audio->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_EVENTCALLBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,0,0,&format,nullptr);
  if(FAILED(status))return status;
  signal=CreateEvent(nullptr,FALSE,FALSE,nullptr);if(!signal)return HRESULT_FROM_WIN32(GetLastError());
  if(FAILED(status=audio->SetEventHandle(signal)))return status;ComPtr<IAudioCaptureClient> capture;
  if(FAILED(status=audio->GetService(IID_PPV_ARGS(&capture))))return status;if(FAILED(status=audio->Start()))return status;
  ready(S_OK);notified=true;
  std::vector<BYTE> silence;
  while(InterlockedCompareExchange(stop,0,0)==0){WaitForSingleObject(signal,50);UINT32 frames=0;
   if(FAILED(status=capture->GetNextPacketSize(&frames)))return status;
   while(frames){BYTE* data=nullptr;DWORD flags=0;if(FAILED(status=capture->GetBuffer(&data,&frames,&flags,nullptr,nullptr)))return status;
    int bytes=frames*4;if(flags&AUDCLNT_BUFFERFLAGS_SILENT){silence.assign(bytes,0);samples(silence.data(),bytes);}else samples(data,bytes);
    status=capture->ReleaseBuffer(frames);if(FAILED(status))return status;if(FAILED(status=capture->GetNextPacketSize(&frames)))return status;
   }
  }return S_OK;
 };
 try{hr=run();}catch(...){hr=E_FAIL;}if(!notified)ready(hr);if(audio)audio->Stop();audio.Reset();if(signal)CloseHandle(signal);CoUninitialize();return hr;
}
