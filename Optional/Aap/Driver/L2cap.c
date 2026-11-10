/*++
    L2cap.c - the L2CAP channel operations, implemented with Bluetooth Request
    Blocks (BRBs) submitted to the stack via IOCTL_INTERNAL_BTH_SUBMIT_BRB.
--*/

#include "NTPodsAAP.h"

//
// Submit a BRB synchronously to the Bluetooth stack (our parent I/O target).
//
NTSTATUS
LpSubmitBrbSync(
    _In_    PDEVICE_CONTEXT Ctx,
    _Inout_ PBRB            Brb
)
{
    NTSTATUS                 status;
    WDFREQUEST               request;
    PIRP                     irp;
    PIO_STACK_LOCATION       stack;
    WDF_REQUEST_SEND_OPTIONS options;

    if (Ctx->IoTarget == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    status = WdfRequestCreate(WDF_NO_OBJECT_ATTRIBUTES, Ctx->IoTarget, &request);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    // bthport reads the BRB pointer from Parameters.Others.Argument1 of the
    // IOCTL_INTERNAL_BTH_SUBMIT_BRB IRP. Set it explicitly on the next stack
    // location (the WDF memory-descriptor path left Argument1 NULL, so bthport
    // dereferenced a NULL BRB -> bugcheck 0x3B in BTHport!IOTracing::TraceStart).
    irp = WdfRequestWdmGetIrp(request);
    stack = IoGetNextIrpStackLocation(irp);
    stack->MajorFunction = IRP_MJ_INTERNAL_DEVICE_CONTROL;
    stack->MinorFunction = 0;
    stack->Parameters.DeviceIoControl.IoControlCode = IOCTL_INTERNAL_BTH_SUBMIT_BRB;
    stack->Parameters.Others.Argument1 = Brb;

    WDF_REQUEST_SEND_OPTIONS_INIT(
        &options, WDF_REQUEST_SEND_OPTION_SYNCHRONOUS | WDF_REQUEST_SEND_OPTION_TIMEOUT);
    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(&options, WDF_REL_TIMEOUT_IN_SEC(10));

    if (WdfRequestSend(request, Ctx->IoTarget, &options)) {
        status = WdfRequestGetStatus(request);
    } else {
        status = WdfRequestGetStatus(request);
    }

    WdfObjectDelete(request);
    return status;
}

//
// Open an outbound L2CAP channel to the remote device on the given PSM.
//
NTSTATUS
LpConnect(
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ BTH_ADDR        Address,
    _In_ USHORT          Psm
)
{
    NTSTATUS                       status;
    struct _BRB_L2CA_OPEN_CHANNEL* brb;

    if (!Ctx->HasBthInterface) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (Ctx->State == LpConnected) {
        if (Ctx->RemoteAddress == Address && Ctx->Psm == Psm) {
            return STATUS_SUCCESS;
        }
        LpDisconnect(Ctx);
    }

    // Basic-mode L2CAP open. ERTM was investigated exhaustively and ruled out: the
    // Windows bthport stack does NOT serialize a Retransmission-and-Flow option to
    // the wire when a profile driver requests ERTM via BRB_L2CA_OPEN_ENHANCED_CHANNEL
    // (CM_RETRANSMISSION_AND_FLOW). Two builds — timeouts 0, then 2000/12000 with
    // RtlZeroMemory + MPS>0 — both produced Configure Requests carrying only MTU +
    // FlushTO (btvs-confirmed); the ERTM option never left the host. The AirPods run
    // AAP over Basic regardless (they counter-propose Basic), as does Android (whose
    // ERTM request is likewise rejected and falls back). So ERTM is not the heart-
    // rate differentiator, and requesting it just breaks the connect on Windows.
    brb = (struct _BRB_L2CA_OPEN_CHANNEL*)
        Ctx->BthInterface.BthAllocateBrb(BRB_L2CA_OPEN_CHANNEL, LP_POOL_TAG);
    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb->BtAddress    = Address;
    brb->Psm          = Psm;
    // Authenticated + encrypted, like the AACP socket upstream android/rewrite opens
    // (auth=true, encrypt=true). An earlier attempt with CF_LINK_ENCRYPTED alone hit
    // a re-authentication the controller rejected (HCI 0x27); with both flags on a
    // freshly paired link the open succeeds (2026-09-29). This is the configuration
    // heart rate was verified with — whether HR strictly needs these flags (vs only
    // the MTU below) is not yet isolated.
    brb->ChannelFlags = CF_ROLE_EITHER | CF_LINK_AUTHENTICATED | CF_LINK_ENCRYPTED;

    // HEART RATE (2026-09-29): advertise an incoming MTU of 1691 in our Configure
    // Request, like Android (Fluoride) and Bumble do. With Flags == 0 the stack sent
    // no MTU option, so the channel ran at the 672-byte default — and the AirPods
    // only publish the RTBuddy services whose descriptor fits the MTU: devmotion6
    // (~521 B) and activity (~573 B) made it, HEARTRATE/HEARTRATEv2 (~920 B) never
    // did, so svc 19 answered kIOReturnBadArgument and no BPM ever arrived. On the
    // same PC under Bumble with MTU 1691 the buds pushed the HR descriptors at once
    // and streamed BPM. The daemon's receive buffer is 8 KiB, so 1691 fits.
    //
    // bthport semantics (verified on the air, HCI ETW 2026-09-29): ConfigIn is the
    // INBOUND direction — it is what goes into OUR Configure Request as the MTU we
    // can receive. ConfigOut only caps what we send (it showed up as MTU 1691 in our
    // Configure *Response*, while our request still carried no MTU → 672 inbound).
    brb->ConfigOut.Flags         = 0;
    brb->ConfigIn.Flags          = CFG_MTU;
    brb->ConfigIn.Mtu.Min        = 672;  // L2CAP default; accept down to it
    brb->ConfigIn.Mtu.Preferred  = 1691; // what Android/Bumble request
    brb->ConfigIn.Mtu.Max        = 1691;
    brb->IncomingQueueDepth = 10; // MS-recommended default

    // Be notified when the remote tears the channel down.
    brb->CallbackFlags   = CALLBACK_DISCONNECT;
    brb->Callback        = LpIndicationCallback;
    brb->CallbackContext = Ctx;
    brb->ReferenceObject = Ctx->WdmDeviceObject;

    WdfSpinLockAcquire(Ctx->Lock);
    Ctx->State         = LpConnecting;
    Ctx->RemoteAddress = Address;
    Ctx->Psm           = Psm;
    WdfSpinLockRelease(Ctx->Lock);

    status = LpSubmitBrbSync(Ctx, (PBRB)brb);

    if (NT_SUCCESS(status)) {
        WdfSpinLockAcquire(Ctx->Lock);
        Ctx->ChannelHandle = brb->ChannelHandle;
        Ctx->State         = LpConnected;
        WdfSpinLockRelease(Ctx->Lock);
        KdPrint(("NTPodsAAP: L2CAP connected (handle=%p)\n", brb->ChannelHandle));
        // NB: the ATT (PSM 0x001F) channel is opened LAZILY — on the first hearing-aid
        // ATT write (see LpAttSend), NOT here. The buds' ATT server is dormant until
        // hearing-assist is enabled, so opening a second L2CAP channel to it on every
        // connect only churns the shared ACL and destabilises the AAP channel / A2DP
        // (it opened, idled, and dropped ~30 s later on every session).
        //
        // NB: we deliberately do NOT register an ATT *server* here to accept the
        // buds' inbound PSM-0x001F connection. bthport rejects registering a server
        // on the reserved ATT PSM with STATUS_INVALID_PARAMETER (0xC000000D) — a
        // profile driver may be an ATT client but not a server (tested 2026-08-12).
        // So the AirPods' inbound GATT connection is unavoidably refused on Windows.
    } else {
        WdfSpinLockAcquire(Ctx->Lock);
        Ctx->State         = LpDisconnected;
        Ctx->ChannelHandle = NULL;
        WdfSpinLockRelease(Ctx->Lock);
        KdPrint(("NTPodsAAP: L2CAP connect failed 0x%08X\n", status));
    }

    Ctx->BthInterface.BthFreeBrb((PBRB)brb);
    return status;
}

//
// Open the ATT (PSM 0x001F) channel to the AirPods as a CLIENT — the same
// outbound BRB_L2CA_OPEN_CHANNEL we use for the AAP channel (0x1001), just a second
// channel on the reserved ATT PSM. This is exactly what Android's ATTManager does
// (createL2capChannel(0x1F)); connecting as a client to a reserved PSM is allowed,
// unlike registering a SERVER on it. The buds accept it (their end is the ATT
// server) and we then read/write the hearing-aid audiogram over handle 0x2A.
//
NTSTATUS
LpDisconnect(_In_ PDEVICE_CONTEXT Ctx)
{
    struct _BRB_L2CA_CLOSE_CHANNEL* brb;
    L2CAP_CHANNEL_HANDLE handle;
    BTH_ADDR addr;
    NTSTATUS status;
    if (!Ctx->HasBthInterface) return STATUS_DEVICE_NOT_READY;
    brb = (struct _BRB_L2CA_CLOSE_CHANNEL*)Ctx->BthInterface.BthAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, LP_POOL_TAG);
    if (!brb) return STATUS_INSUFFICIENT_RESOURCES;
    WdfSpinLockAcquire(Ctx->Lock);
    handle = Ctx->ChannelHandle;
    addr = Ctx->RemoteAddress;
    WdfSpinLockRelease(Ctx->Lock);
    if (!handle) { Ctx->BthInterface.BthFreeBrb((PBRB)brb); return STATUS_SUCCESS; }
    brb->BtAddress = addr;
    brb->ChannelHandle = handle;
    status = LpSubmitBrbSync(Ctx, (PBRB)brb);
    Ctx->BthInterface.BthFreeBrb((PBRB)brb);
    if (NT_SUCCESS(status)) {
        WdfSpinLockAcquire(Ctx->Lock);
        if (Ctx->ChannelHandle == handle) { Ctx->ChannelHandle = NULL; Ctx->State = LpDisconnected; }
        WdfSpinLockRelease(Ctx->Lock);
    }
    // Retain failed close state for retry; do not silently lose a live handle.
    return status;
}

NTSTATUS
LpSend(
    _In_ PDEVICE_CONTEXT Ctx,
    _In_ PVOID           Buffer,
    _In_ ULONG           Length
)
{
    NTSTATUS                       status;
    struct _BRB_L2CA_ACL_TRANSFER* brb;

    if (Ctx->State != LpConnected || Ctx->ChannelHandle == NULL) {
        return STATUS_DEVICE_NOT_CONNECTED;
    }
    if (Buffer == NULL || Length == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    brb = (struct _BRB_L2CA_ACL_TRANSFER*)
        Ctx->BthInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, LP_POOL_TAG);
    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb->BtAddress     = Ctx->RemoteAddress;
    brb->ChannelHandle = Ctx->ChannelHandle;
    brb->TransferFlags = ACL_TRANSFER_DIRECTION_OUT;
    brb->Buffer        = Buffer;
    brb->BufferMDL     = NULL;
    brb->BufferSize    = Length;
    brb->Timeout       = 0;

    status = LpSubmitBrbSync(Ctx, (PBRB)brb);
    Ctx->BthInterface.BthFreeBrb((PBRB)brb);
    return status;
}

//
// Read bytes from the channel (blocking up to TimeoutMs).
//
NTSTATUS
LpReceive(
    _In_  PDEVICE_CONTEXT Ctx,
    _Out_ PVOID           Buffer,
    _In_  ULONG           BufferLen,
    _Out_ PULONG          BytesRead,
    _In_  ULONG           TimeoutMs
)
{
    NTSTATUS                       status;
    struct _BRB_L2CA_ACL_TRANSFER* brb;

    *BytesRead = 0;

    if (Ctx->State != LpConnected || Ctx->ChannelHandle == NULL) {
        return STATUS_DEVICE_NOT_CONNECTED;
    }
    if (Buffer == NULL || BufferLen == 0) {
        return STATUS_INVALID_PARAMETER;
    }

    brb = (struct _BRB_L2CA_ACL_TRANSFER*)
        Ctx->BthInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, LP_POOL_TAG);
    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb->BtAddress     = Ctx->RemoteAddress;
    brb->ChannelHandle = Ctx->ChannelHandle;
    brb->TransferFlags = ACL_TRANSFER_DIRECTION_IN | ACL_SHORT_TRANSFER_OK | ACL_TRANSFER_TIMEOUT;
    brb->Buffer        = Buffer;
    brb->BufferMDL     = NULL;
    brb->BufferSize    = BufferLen;
    brb->Timeout       = (LONGLONG)(TimeoutMs ? TimeoutMs : LP_RECV_TIMEOUT_MS);

    status = LpSubmitBrbSync(Ctx, (PBRB)brb);
    if (NT_SUCCESS(status)) {
        *BytesRead = brb->BufferSize; // updated with bytes actually read
    }

    Ctx->BthInterface.BthFreeBrb((PBRB)brb);
    return status;
}

//
// Notifications from the stack (we only care about remote disconnect).
//
VOID
LpIndicationCallback(
    _In_opt_ PVOID                 Context,
    _In_     INDICATION_CODE       Indication,
    _In_     PINDICATION_PARAMETERS Parameters
)
{
    PDEVICE_CONTEXT ctx = (PDEVICE_CONTEXT)Context;

    UNREFERENCED_PARAMETER(Parameters);

    if (ctx == NULL) {
        return;
    }

    switch (Indication) {
    case IndicationRemoteDisconnect:
        WdfSpinLockAcquire(ctx->Lock);
        ctx->State         = LpDisconnected;
        ctx->ChannelHandle = NULL;
        WdfSpinLockRelease(ctx->Lock);
        KdPrint(("NTPodsAAP: remote disconnected the channel\n"));
        break;
    default:
        break;
    }
}

//
// Register an L2CAP server on PSM 0x001F (ATT). The AirPods connect INBOUND here
// for hearing-aid configuration; without a registered server bthport answers their
// Connection Request with "PSM not supported" and the config channel never opens.
// STEP 1: register + log the connect indication (proves the path). Accepting the
// channel (BRB_L2CA_OPEN_CHANNEL_RESPONSE) is Step 2. Non-fatal to the AAP channel.
//
