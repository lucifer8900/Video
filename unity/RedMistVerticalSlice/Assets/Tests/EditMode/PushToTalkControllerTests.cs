using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class PushToTalkControllerTests
    {
        [Test]
        public void PressWithoutDevicesFallsBackAndNeverStarts()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Capture.Devices.Clear();

            VoiceCaptureUpdate update = fixture.Controller.Press();

            Assert.AreEqual(VoiceCaptureState.Fallback, update.State);
            Assert.AreEqual(VoiceCaptureError.NoMicrophone, update.Error);
            Assert.IsTrue(update.ShouldUseFixedChoices);
            Assert.AreEqual(0, fixture.Capture.StartCount);
        }

        [Test]
        public void DeniedPermissionCallbackFallsBackAndNeverStarts()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Unknown);
            VoiceCaptureUpdate waiting = fixture.Controller.Press();
            Assert.AreEqual(VoiceCaptureState.AwaitingPermission, waiting.State);

            fixture.Permission.Complete(VoicePermissionStatus.Denied);

            Assert.AreEqual(VoiceCaptureState.Fallback, fixture.Controller.Current.State);
            Assert.AreEqual(VoiceCaptureError.PermissionDenied, fixture.Controller.Current.Error);
            Assert.IsTrue(fixture.Controller.Current.ShouldUseFixedChoices);
            Assert.AreEqual(0, fixture.Capture.StartCount);
        }

        [Test]
        public void DisconnectWhileRecordingCancelsAndFallsBack()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            Assert.AreEqual(VoiceCaptureState.Recording, fixture.Controller.Press().State);
            fixture.Capture.Devices.Clear();

            VoiceCaptureUpdate update = fixture.Controller.Tick();

            Assert.AreEqual(VoiceCaptureError.DeviceDisconnected, update.Error);
            Assert.IsTrue(update.ShouldUseFixedChoices);
            Assert.AreEqual(1, fixture.Capture.CancelCount);
        }

        [Test]
        public void MaximumDurationCancelsWithTimeoutAndFallsBack()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Controller.Press();
            fixture.Clock.Now = 8.01;

            VoiceCaptureUpdate update = fixture.Controller.Tick();

            Assert.AreEqual(VoiceCaptureState.Fallback, update.State);
            Assert.AreEqual(VoiceCaptureError.Timeout, update.Error);
            Assert.IsTrue(update.ShouldUseFixedChoices);
            Assert.AreEqual(1, fixture.Capture.CancelCount);
        }

        [Test]
        public void PressThenReleaseReturnsCapturedAudioExactlyOnce()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Capture.Payload = new VoiceAudioPayload(new[] { 0.1f, -0.2f, 0.15f }, 16000, 1);
            int captured = 0;
            fixture.Controller.Captured += _ => captured++;
            fixture.Controller.Press();

            VoiceCaptureUpdate first = fixture.Controller.Release();
            VoiceCaptureUpdate second = fixture.Controller.Release();

            Assert.AreEqual(VoiceCaptureState.Completed, first.State);
            Assert.IsTrue(first.HasAudio);
            Assert.IsFalse(first.ShouldUseFixedChoices);
            Assert.AreEqual(VoiceCaptureState.Completed, second.State);
            Assert.AreEqual(1, captured);
            Assert.AreEqual(1, fixture.Capture.StopCount);
        }

        [Test]
        public void ReleaseBeforePermissionCompletesCancelsAndLateCallbackIsIgnored()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Unknown);
            fixture.Controller.Press();

            VoiceCaptureUpdate released = fixture.Controller.Release();
            fixture.Permission.Complete(VoicePermissionStatus.Granted);

            Assert.AreEqual(VoiceCaptureError.Cancelled, released.Error);
            Assert.IsTrue(released.ShouldUseFixedChoices);
            Assert.AreEqual(VoiceCaptureState.Fallback, fixture.Controller.Current.State);
            Assert.AreEqual(0, fixture.Capture.StartCount);
        }

        [Test]
        public void SilentCaptureFallsBackInsteadOfReturningAudio()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Capture.Payload = new VoiceAudioPayload(new[] { 0.0001f, -0.0002f }, 16000, 1);
            fixture.Controller.Press();

            VoiceCaptureUpdate update = fixture.Controller.Release();

            Assert.AreEqual(VoiceCaptureError.Silence, update.Error);
            Assert.IsFalse(update.HasAudio);
            Assert.IsTrue(update.ShouldUseFixedChoices);
        }

        [Test]
        public void GatewayExceptionIsConvertedToFailureAndDoesNotEscape()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Capture.ThrowOnStart = true;

            VoiceCaptureUpdate update = default;
            Assert.DoesNotThrow(() => update = fixture.Controller.Press());

            Assert.AreEqual(VoiceCaptureState.Fallback, update.State);
            Assert.AreEqual(VoiceCaptureError.StartFailed, update.Error);
            Assert.IsTrue(update.ShouldUseFixedChoices);
        }

        [Test]
        public void RepeatedPressAndIdleReleaseDoNotCrashOrDoubleStart()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);

            Assert.DoesNotThrow(() => fixture.Controller.Release());
            fixture.Controller.Press();
            fixture.Controller.Press();
            fixture.Controller.Press();

            Assert.AreEqual(1, fixture.Capture.StartCount);
            Assert.AreEqual(VoiceCaptureState.Recording, fixture.Controller.Current.State);
        }

        [Test]
        public void RecordingTickClampsVolumeFeedbackToZeroOne()
        {
            Fixture fixture = Fixture.Create(VoicePermissionStatus.Granted);
            fixture.Controller.Press();
            fixture.Capture.Level = 4.5f;
            Assert.AreEqual(1f, fixture.Controller.Tick().Level01);
            fixture.Capture.Level = -2f;
            Assert.AreEqual(0f, fixture.Controller.Tick().Level01);
        }

        private sealed class Fixture
        {
            public FakeClock Clock;
            public FakePermission Permission;
            public FakeCapture Capture;
            public PushToTalkController Controller;

            public static Fixture Create(VoicePermissionStatus permission)
            {
                var fixture = new Fixture
                {
                    Clock = new FakeClock(),
                    Permission = new FakePermission(permission),
                    Capture = new FakeCapture()
                };
                fixture.Controller = new PushToTalkController(
                    fixture.Permission,
                    fixture.Capture,
                    fixture.Clock,
                    new VoiceCaptureOptions(16000, 8d, 5d, 0.5d, 0.01f));
                return fixture;
            }
        }

        private sealed class FakeClock : IVoiceClock
        {
            public double Now;
            public double NowSeconds => Now;
        }

        private sealed class FakePermission : IVoicePermissionGateway
        {
            private Action<VoicePermissionStatus> _completion;

            public FakePermission(VoicePermissionStatus current)
            {
                Current = current;
            }

            public VoicePermissionStatus Current { get; private set; }

            public void Request(Action<VoicePermissionStatus> completed)
            {
                _completion = completed;
            }

            public void Complete(VoicePermissionStatus status)
            {
                Current = status;
                Action<VoicePermissionStatus> completion = _completion;
                _completion = null;
                completion?.Invoke(status);
            }
        }

        private sealed class FakeCapture : IVoiceCaptureGateway
        {
            public readonly List<string> Devices = new List<string> { "fake-mic" };
            public int StartCount;
            public int StopCount;
            public int CancelCount;
            public bool Recording = true;
            public bool ThrowOnStart;
            public float Level;
            public VoiceAudioPayload Payload = new VoiceAudioPayload(new[] { 0.1f }, 16000, 1);

            public IReadOnlyList<string> DeviceIds => Devices;

            public bool TryStart(string deviceId, int sampleRateHz, int maxSeconds)
            {
                StartCount++;
                if (ThrowOnStart) throw new InvalidOperationException("fake start failure");
                Recording = true;
                return true;
            }

            public bool IsRecording(string deviceId) => Recording;
            public float ReadLevel01(string deviceId) => Level;

            public VoiceAudioPayload Stop(string deviceId)
            {
                StopCount++;
                Recording = false;
                return Payload;
            }

            public void Cancel(string deviceId)
            {
                CancelCount++;
                Recording = false;
            }
        }
    }
}
