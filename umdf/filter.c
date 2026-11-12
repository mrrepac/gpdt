#define WIN32_NO_STATUS
#include <windows.h>
#undef WIN32_NO_STATUS
#include <wdf.h>
#include <hidclass.h>
#include <initguid.h>
#include "../driver/policy.h"
#define VERSION 3u
#define SET_MODE 0x22e000u
#define GET_STATUS 0x226004u
DEFINE_GUID(GUID_GPDT_UMDF,0x0498b91a,0x6753,0x43d7,0xa4,0x83,0x25,0x9c,0xa8,0xe6,0x70,0x19);
typedef struct { ULONG Version,Mode,Attached,Ready; ULONGLONG Reads,Matches,Suppressed,OwnerReports,Unexpected; } STATUS_DATA;
typedef struct { SRWLOCK Lock; UCHAR Report; ULONG Mask,Pid; WDFFILEOBJECT Owner; ULONGLONG Deadline,Generation; STATUS_DATA Status; } DEVICE;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE,Context)
typedef struct { ULONG Pid; ULONGLONG Generation; } REQUEST;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(REQUEST,RequestContext)
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD AddDevice;
EVT_WDF_IO_QUEUE_IO_READ ReadRequest;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL ControlRequest;
EVT_WDF_REQUEST_COMPLETION_ROUTINE CompleteRead;
EVT_WDF_FILE_CLEANUP CloseFile;
EVT_WDF_DEVICE_D0_EXIT PowerExit;
EVT_WDF_REQUEST_IMPERSONATE CheckAdmin;
static void Reset(DEVICE* d) { d->Status.Mode=0;d->Status.Ready=0;d->Deadline=0;d->Generation++; }
void CheckAdmin(WDFREQUEST request,PVOID context) {
    SID_IDENTIFIER_AUTHORITY authority=SECURITY_NT_AUTHORITY;PSID sid=NULL;BOOL member=FALSE;
    UNREFERENCED_PARAMETER(request);
    if(AllocateAndInitializeSid(&authority,2,SECURITY_BUILTIN_DOMAIN_RID,DOMAIN_ALIAS_RID_ADMINS,0,0,0,0,0,0,&sid)) {
        if(!CheckTokenMembership(NULL,sid,&member))member=FALSE;FreeSid(sid);
    }
    *(BOOL*)context=member;
}
void CloseFile(WDFFILEOBJECT file) {
    DEVICE* d=Context(WdfFileObjectGetDevice(file));
    AcquireSRWLockExclusive(&d->Lock);
    if(d->Owner==file){Reset(d);d->Owner=NULL;d->Pid=0;}
    ReleaseSRWLockExclusive(&d->Lock);
}
NTSTATUS PowerExit(WDFDEVICE device,WDF_POWER_DEVICE_STATE state) {
    DEVICE* d=Context(device);UNREFERENCED_PARAMETER(state);
    AcquireSRWLockExclusive(&d->Lock);Reset(d);ReleaseSRWLockExclusive(&d->Lock);return STATUS_SUCCESS;
}
void CompleteRead(WDFREQUEST request,WDFIOTARGET target,PWDF_REQUEST_COMPLETION_PARAMS params,WDFCONTEXT context) {
    DEVICE* d=Context((WDFDEVICE)context);REQUEST* r=RequestContext(request);
    PVOID buffer=NULL;size_t capacity=0,length=(size_t)params->IoStatus.Information;
    UNREFERENCED_PARAMETER(target);
    if(NT_SUCCESS(params->IoStatus.Status) && length && NT_SUCCESS(WdfRequestRetrieveOutputBuffer(request,1,&buffer,&capacity)) && length<=capacity) {
        AcquireSRWLockExclusive(&d->Lock);d->Status.Reads++;
        if(GpdtReportMatches(d->Report,buffer,length)) {
            d->Status.Matches++;
            if(d->Pid && r->Pid==d->Pid && GetTickCount64()<d->Deadline)d->Status.OwnerReports++;
            else if(GpdtShouldNeutralize(d->Status.Mode,d->Pid,r->Pid,GetTickCount64(),d->Deadline)) {
                ((UCHAR*)buffer)[1]=0;d->Status.Suppressed++;
            }
        }else d->Status.Unexpected++;
        ReleaseSRWLockExclusive(&d->Lock);
    }else if(!NT_SUCCESS(params->IoStatus.Status)) {
        AcquireSRWLockExclusive(&d->Lock);
        if(r->Pid==d->Pid && r->Generation==d->Generation)Reset(d);
        ReleaseSRWLockExclusive(&d->Lock);
    }
    WdfRequestCompleteWithInformation(request,params->IoStatus.Status,params->IoStatus.Information);
}
static void Forward(WDFDEVICE device,WDFREQUEST request,BOOL observe,BOOL reader) {
    DEVICE* d=Context(device);
    if(observe) {
        WDF_OBJECT_ATTRIBUTES attributes;REQUEST* r;NTSTATUS status;ULONG pid;ULONGLONG generation;
        WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes,REQUEST);
        status=WdfObjectAllocateContext(request,&attributes,(PVOID*)&r);
        if(NT_SUCCESS(status) || status==STATUS_OBJECT_NAME_EXISTS) {
            r=RequestContext(request);pid=r->Pid=WdfRequestGetRequestorProcessId(request);
            AcquireSRWLockExclusive(&d->Lock);generation=r->Generation=d->Generation;ReleaseSRWLockExclusive(&d->Lock);
            WdfRequestFormatRequestUsingCurrentType(request);
            WdfRequestSetCompletionRoutine(request,CompleteRead,device);
            if(!WdfRequestSend(request,WdfDeviceGetIoTarget(device),NULL)) {
                AcquireSRWLockExclusive(&d->Lock);if(pid==d->Pid && generation==d->Generation)Reset(d);ReleaseSRWLockExclusive(&d->Lock);
                WdfRequestComplete(request,WdfRequestGetStatus(request));
            }else if(reader) {
                AcquireSRWLockExclusive(&d->Lock);
                if(pid && pid==d->Pid && generation==d->Generation && d->Status.Mode && GetTickCount64()<d->Deadline)d->Status.Ready=d->Mask;
                ReleaseSRWLockExclusive(&d->Lock);
            }
            return;
        }
    }
    { WDF_REQUEST_SEND_OPTIONS options;WDF_REQUEST_SEND_OPTIONS_INIT(&options,WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET);
      if(!WdfRequestSend(request,WdfDeviceGetIoTarget(device),&options))WdfRequestComplete(request,WdfRequestGetStatus(request)); }
}
void ReadRequest(WDFQUEUE queue,WDFREQUEST request,size_t length) {Forward(WdfIoQueueGetDevice(queue),request,TRUE,length==2);}
void ControlRequest(WDFQUEUE queue,WDFREQUEST request,size_t outputLength,size_t inputLength,ULONG code) {
    WDFDEVICE device=WdfIoQueueGetDevice(queue);DEVICE* d=Context(device);
    NTSTATUS status;size_t returned=0;ULONG* input=NULL;STATUS_DATA* output=NULL;
    WDFFILEOBJECT file=WdfRequestGetFileObject(request);ULONG pid=WdfRequestGetRequestorProcessId(request);
    UNREFERENCED_PARAMETER(outputLength);UNREFERENCED_PARAMETER(inputLength);
    if(code!=SET_MODE && code!=GET_STATUS){Forward(device,request,code==IOCTL_HID_GET_INPUT_REPORT,FALSE);return;}
    if(code==SET_MODE) {
        BOOL admin=FALSE;
        status=WdfRequestRetrieveInputBuffer(request,8,(PVOID*)&input,NULL);
        if(NT_SUCCESS(status))status=WdfRequestImpersonate(request,SecurityIdentification,CheckAdmin,&admin);
        if(NT_SUCCESS(status) && (!admin || !file || !pid))status=STATUS_ACCESS_DENIED;
    }else status=WdfRequestRetrieveOutputBuffer(request,sizeof(*output),(PVOID*)&output,NULL);
    if(!NT_SUCCESS(status)){WdfRequestComplete(request,status);return;}
    AcquireSRWLockExclusive(&d->Lock);
    if(input) {
        if(input[0]!=VERSION || input[1]>2)status=STATUS_INVALID_PARAMETER;
        else if(d->Owner && d->Owner!=file && GetTickCount64()<d->Deadline)status=STATUS_SHARING_VIOLATION;
        else if(input[1]==2 && (d->Owner!=file || d->Pid!=pid || d->Status.Ready!=d->Mask || GetTickCount64()>=d->Deadline))status=STATUS_DEVICE_NOT_READY;
        else {
            if(d->Owner!=file || d->Pid!=pid){Reset(d);d->Owner=file;d->Pid=pid;}
            d->Status.Mode=input[1];d->Deadline=GetTickCount64()+2000;
            if(!input[1]){Reset(d);d->Owner=NULL;d->Pid=0;}
        }
    }else { *output=d->Status;if(GetTickCount64()>=d->Deadline)output->Mode=0;returned=sizeof(*output); }
    ReleaseSRWLockExclusive(&d->Lock);
    WdfRequestCompleteWithInformation(request,status,returned);
}
NTSTATUS AddDevice(WDFDRIVER driver,PWDFDEVICE_INIT init) {
    WCHAR ids[512]={0};ULONG required,mask=0;UCHAR report=0;WCHAR* id;NTSTATUS status;
    WDFDEVICE device;WDF_OBJECT_ATTRIBUTES attributes;WDF_IO_QUEUE_CONFIG queue;WDF_FILEOBJECT_CONFIG files;WDF_PNPPOWER_EVENT_CALLBACKS power;DEVICE* d;
    UNREFERENCED_PARAMETER(driver);
    status=WdfFdoInitQueryProperty(init,DevicePropertyHardwareID,sizeof(ids)-sizeof(WCHAR),ids,&required);
    if(!NT_SUCCESS(status))return status;
    for(id=ids;*id;id+=wcslen(id)+1) {
        if(!_wcsicmp(id,L"HID\\INTC816&Col0D")){mask=1;report=0x14;}
        if(!_wcsicmp(id,L"HID\\INTC816&Col0E")){mask=2;report=0x15;}
    }
    if(!mask)return STATUS_NOT_SUPPORTED;
    WdfFdoInitSetFilter(init);
    WDF_FILEOBJECT_CONFIG_INIT(&files,NULL,NULL,CloseFile);files.AutoForwardCleanupClose=WdfTrue;
    WdfDeviceInitSetFileObjectConfig(init,&files,WDF_NO_OBJECT_ATTRIBUTES);
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&power);power.EvtDeviceD0Exit=PowerExit;WdfDeviceInitSetPnpPowerEventCallbacks(init,&power);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes,DEVICE);
    status=WdfDeviceCreate(&init,&attributes,&device);if(!NT_SUCCESS(status))return status;
    d=Context(device);InitializeSRWLock(&d->Lock);d->Mask=mask;d->Report=report;d->Status.Version=VERSION;d->Status.Attached=mask;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue,WdfIoQueueDispatchParallel);queue.PowerManaged=WdfFalse;queue.EvtIoRead=ReadRequest;queue.EvtIoDeviceControl=ControlRequest;
    status=WdfIoQueueCreate(device,&queue,WDF_NO_OBJECT_ATTRIBUTES,NULL);if(!NT_SUCCESS(status))return status;
    return WdfDeviceCreateDeviceInterface(device,&GUID_GPDT_UMDF,NULL);
}
NTSTATUS DriverEntry(PDRIVER_OBJECT object,PUNICODE_STRING path) {
    WDF_DRIVER_CONFIG config;WDF_DRIVER_CONFIG_INIT(&config,AddDevice);
    return WdfDriverCreate(object,path,WDF_NO_OBJECT_ATTRIBUTES,&config,WDF_NO_HANDLE);
}
