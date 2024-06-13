using core_engine.ISO8601Date;

namespace core_engine_tests;

public class DateExpressionTest
{
    [Test]
        public void Test_ParseIso8601Duration_YearsMonthsDays()
        {
            string isoDuration = "P1Y2M10D";
            TimeSpan expected = new TimeSpan(1 * 365 + 2 * 30 + 10, 0, 0, 0);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_Weeks()
        {
            string isoDuration = "P3W";
            TimeSpan expected = new TimeSpan(3 * 7, 0, 0, 0);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_DaysHoursMinutesSeconds()
        {
            string isoDuration = "P10DT2H30M15S";
            TimeSpan expected = new TimeSpan(10, 2, 30, 15);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_HoursMinutes()
        {
            string isoDuration = "PT2H30M";
            TimeSpan expected = new TimeSpan(0, 2, 30, 0);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_Empty()
        {
            string isoDuration = "P";
            TimeSpan expected = TimeSpan.Zero;
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_OnlyTime()
        {
            string isoDuration = "PT30M";
            TimeSpan expected = new TimeSpan(0, 0, 30, 0);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_OnlyDate()
        {
            string isoDuration = "P1Y2M";
            TimeSpan expected = new TimeSpan(1 * 365 + 2 * 30, 0, 0, 0);
            TimeSpan actual = DateExtensions.ParseIso8601Duration(isoDuration);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Test_ParseIso8601Duration_InvalidFormat()
        {
            string isoDuration = "InvalidFormat";
            Assert.Throws<ArgumentException>(() => DateExtensions.ParseIso8601Duration(isoDuration));
        }
}