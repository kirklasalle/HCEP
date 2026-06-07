// ──────────────────────────────────────────────────────────────
// HCEP SDK — Unreal Engine Gaze Controller Component
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "IWebSocket.h"
#include "HcepGazeController.generated.h"

/**
 * UActorComponent that connects to the HCEP WebSocket stream
 * and exposes real-time Gaze Direction and Head Rotation to Blueprints/AnimGraph.
 */
UCLASS(ClassGroup=(Custom), meta=(BlueprintSpawnableComponent))
class HCEP_API UHcepGazeController : public UActorComponent
{
    GENERATED_BODY()

public:
    UHcepGazeController();

protected:
    virtual void BeginPlay() override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

public:
    virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;

    /** HCEP WebSocket Server Address */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HCEP Connection")
    FString ServerUrl;

    /** Target Gaze Direction Vector (normalized, in Unreal coordinate space) */
    UPROPERTY(BlueprintReadOnly, Category = "HCEP Tracking Data")
    FVector TargetGazeDirection;

    /** Target Head Rotation (Pitch, Yaw, Roll, in Unreal coordinate space) */
    UPROPERTY(BlueprintReadOnly, Category = "HCEP Tracking Data")
    FRotator TargetHeadRotation;

    /** Smoothing speed for interpolation */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HCEP Tracking Data", meta = (ClampMin = "0.1", ClampMax = "20.0"))
    float InterpolationSpeed;

private:
    void InitializeWebSocket();
    void CleanupWebSocket();

    void OnWebSocketConnected();
    void OnWebSocketConnectionError(const FString& Error);
    void OnWebSocketClosed(int32 StatusCode, const FString& Reason, bool bWasClean);
    void OnWebSocketMessageReceived(const FString& MessageString);

    TSharedPtr<IWebSocket> WebSocket;
    
    // Internal interpolated tracking values
    FVector CurrentGazeDirection;
    FRotator CurrentHeadRotation;
};
