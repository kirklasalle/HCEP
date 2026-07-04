// ──────────────────────────────────────────────────────────────
// HCEP SDK — Unreal Engine Gaze Controller Component
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// LICENSE NOTICE (MIT):
// This SDK component is open-source and permissively licensed under
// the MIT License. It is designed to interface with the proprietary
// HCEP core engine. For core engine licensing terms, see the LICENSE
// file in the root of the HCEP repository.
// ──────────────────────────────────────────────────────────────
#include "HcepGazeController.h"
#include "WebSocketsModule.h"
#include "Json.h"
#include "Serialization/JsonSerializer.h"

UHcepGazeController::UHcepGazeController()
{
    PrimaryComponentTick.bCanEverTick = true;

    ServerUrl = TEXT("ws://localhost:5000/ws/stream");
    InterpolationSpeed = 10.0f;

    TargetGazeDirection = FVector::ForwardVector;
    TargetHeadRotation = FRotator::ZeroRotator;
    
    CurrentGazeDirection = FVector::ForwardVector;
    CurrentHeadRotation = FRotator::ZeroRotator;
}

void UHcepGazeController::BeginPlay()
{
    Super::BeginPlay();

    InitializeWebSocket();
}

void UHcepGazeController::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    CleanupWebSocket();

    Super::EndPlay(EndPlayReason);
}

void UHcepGazeController::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
    Super::TickComponent(DeltaTime, TickType, ThisTickFunction);

    // Smoothly interpolate current gaze and head values towards targets
    CurrentGazeDirection = FMath::VInterpTo(CurrentGazeDirection, TargetGazeDirection, DeltaTime, InterpolationSpeed);
    CurrentHeadRotation = FMath::RInterpTo(CurrentHeadRotation, TargetHeadRotation, DeltaTime, InterpolationSpeed);

    // Expose interpolated tracking variables to blueprints
    // E.g., developer can retrieve UHcepGazeController->CurrentGazeDirection inside AnimInstance
}

void UHcepGazeController::InitializeWebSocket()
{
    if (!FWebSocketsModule::IsAvailable())
    {
        UE_LOG(LogTemp, Warning, TEXT("[HCEP Unreal SDK] WebSockets module is not loaded!"));
        return;
    }

    WebSocket = FWebSocketsModule::Get().CreateWebSocket(ServerUrl, TEXT("ws"));

    WebSocket->OnConnected().AddUObject(this, &UHcepGazeController::OnWebSocketConnected);
    WebSocket->OnConnectionError().AddUObject(this, &UHcepGazeController::OnWebSocketConnectionError);
    WebSocket->OnClosed().AddUObject(this, &UHcepGazeController::OnWebSocketClosed);
    WebSocket->OnMessage().AddUObject(this, &UHcepGazeController::OnWebSocketMessageReceived);

    WebSocket->Connect();
    UE_LOG(LogTemp, Log, TEXT("[HCEP Unreal SDK] Connecting to HCEP server at: %s"), *ServerUrl);
}

void UHcepGazeController::CleanupWebSocket()
{
    if (WebSocket.IsValid() && WebSocket->IsConnected())
    {
        WebSocket->Close();
    }
    WebSocket.Reset();
}

void UHcepGazeController::OnWebSocketConnected()
{
    UE_LOG(LogTemp, Log, TEXT("[HCEP Unreal SDK] Successfully connected to HCEP WebSocket stream."));
}

void UHcepGazeController::OnWebSocketConnectionError(const FString& Error)
{
    UE_LOG(LogTemp, Error, TEXT("[HCEP Unreal SDK] WebSocket connection error: %s"), *Error);
}

void UHcepGazeController::OnWebSocketClosed(int32 StatusCode, const FString& Reason, bool bWasClean)
{
    UE_LOG(LogTemp, Log, TEXT("[HCEP Unreal SDK] WebSocket closed. Status: %d, Reason: %s"), StatusCode, *Reason);
}

void UHcepGazeController::OnWebSocketMessageReceived(const FString& MessageString)
{
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(MessageString);

    if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
    {
        TSharedPtr<FJsonObject> TargetObject = JsonObject;

        // Check if there is a trust envelope
        if (JsonObject->HasField(TEXT("trust")))
        {
            TSharedPtr<FJsonObject> TrustObj = JsonObject->GetObjectField(TEXT("trust"));
            if (TrustObj.IsValid())
            {
                FString SigningState = TrustObj->GetStringField(TEXT("signing_state"));
                if (SigningState == TEXT("invalid"))
                {
                    UE_LOG(LogTemp, Warning, TEXT("[HCEP Unreal SDK] Telemetry Trust is INVALID. Active Directives may be tampered."));
                }
            }
        }

        // Unwrap payload if it exists (trust envelope format)
        if (JsonObject->HasField(TEXT("payload")))
        {
            TargetObject = JsonObject->GetObjectField(TEXT("payload"));
        }

        // Check if a person is currently tracked
        if (TargetObject.IsValid() && TargetObject->HasField(TEXT("primaryPerson")) && !TargetObject->IsNullField(TEXT("primaryPerson")))
        {
            TSharedPtr<FJsonObject> PrimaryPerson = TargetObject->GetObjectField(TEXT("primaryPerson"));
            if (PrimaryPerson.IsValid())
            {
                // 1. Extract Gaze Direction Vector (x, y, z)
                if (PrimaryPerson->HasField(TEXT("gazeDirection")))
                {
                    TSharedPtr<FJsonObject> Gaze = PrimaryPerson->GetObjectField(TEXT("gazeDirection"));
                    if (Gaze.IsValid())
                    {
                        double GazeX = Gaze->GetNumberField(TEXT("x"));
                        double GazeY = Gaze->GetNumberField(TEXT("y"));
                        double GazeZ = Gaze->GetNumberField(TEXT("z"));

                        // Map OpenCV coords (+X right, +Y up, +Z forward) to Unreal coords (+X forward, +Y right, +Z up)
                        // OpenCV X -> Unreal Y
                        // OpenCV Y -> Unreal Z
                        // OpenCV Z -> Unreal X
                        TargetGazeDirection = FVector(GazeZ, GazeX, GazeY).GetSafeNormal();
                    }
                }

                // 2. Extract Head Rotation Rotator (pitch, yaw, roll)
                if (PrimaryPerson->HasField(TEXT("headRotation")))
                {
                    TSharedPtr<FJsonObject> HeadRot = PrimaryPerson->GetObjectField(TEXT("headRotation"));
                    if (HeadRot.IsValid())
                    {
                        double Pitch = HeadRot->GetNumberField(TEXT("pitch"));
                        double Yaw = HeadRot->GetNumberField(TEXT("yaw"));
                        double Roll = HeadRot->GetNumberField(TEXT("roll"));

                        // Unreal FRotator constructor takes (Pitch, Yaw, Roll)
                        TargetHeadRotation = FRotator(Pitch, Yaw, Roll);
                    }
                }
            }
        }
    }
}
