using System;
using NUnit.Framework;

namespace Naraka.Core.Domain.Tests
{
    public sealed class RequestIdTests
    {
        [Test]
        public void EmptyValueIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new RequestId(" "));
        }

        [Test]
        public void EqualValuesAreEqual()
        {
            Assert.That(new RequestId("request-1"), Is.EqualTo(new RequestId("request-1")));
        }
    }
}
