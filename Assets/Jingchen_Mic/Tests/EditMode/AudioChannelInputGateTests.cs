using NUnit.Framework;

namespace Jingchen.Mic.Tests
{
    public sealed class AudioChannelInputGateTests
    {
        private AudioChannelInputGate gate;

        [SetUp]
        public void SetUp()
        {
            gate = new AudioChannelInputGate();
        }

        [Test]
        public void JoiningWithButtonsHeldRequiresReleaseBeforeEitherAction()
        {
            Check(true, true, true, true, true, false, false);
            Check(true, true, true, true, true, false, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            Check(true, true, false, true, true, false, true);
        }

        [Test]
        public void HoldingTalkTransmitsUntilTheButtonIsReleased()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            Check(true, true, true, true, false, true, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
        }

        [Test]
        public void FocusPauseOrConnectionLossMutesAndRequiresAFreshPress()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            Check(false, true, true, true, true, false, false);
            Check(true, true, true, true, true, false, false);
            Check(true, true, true, true, true, false, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
        }

        [Test]
        public void RightTrackingLossMutesAndHeldTrackingRecoveryCannotResumeTalk()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            Check(true, false, true, true, false, false, false);
            Check(true, true, true, true, false, false, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
        }

        [Test]
        public void ChannelPressCyclesOnceAndCancelsAHeldMicrophone()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            Check(true, true, true, true, true, false, true);
            Check(true, true, true, true, true, false, false);
            Check(true, true, true, true, false, false, false);
            Check(true, true, true, true, true, false, true);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
        }

        [Test]
        public void LeftTrackingRecoveryDoesNotInventAChannelPress()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, false, true, true, false);
            Check(true, true, true, true, true, true, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, false, true, true, false, true);
        }

        [Test]
        public void ResetRequiresButtonReleaseAfterDisableOrDestroy()
        {
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
            gate.Reset();
            Check(true, true, true, true, true, false, false);
            Check(true, true, false, true, false, false, false);
            Check(true, true, true, true, false, true, false);
        }

        private void Check(bool usable, bool rightValid, bool talkPressed,
            bool leftValid, bool channelPressed, bool expectedHeld, bool expectedCycle)
        {
            gate.Sample(usable, rightValid, talkPressed, leftValid, channelPressed,
                out bool held, out bool cycle);
            Assert.That(held, Is.EqualTo(expectedHeld), "Unexpected microphone permission.");
            Assert.That(cycle, Is.EqualTo(expectedCycle), "Unexpected channel switch.");
        }
    }
}
