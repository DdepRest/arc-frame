using System;

namespace MosquitoNetCalculator.Services
{
    public static class AmountInWordsService
    {
        private static readonly string[] Ones = {
            "", "один", "два", "три", "четыре", "пять",
            "шесть", "семь", "восемь", "девять"
        };

        private static readonly string[] OnesFem = {
            "", "одна", "две", "три", "четыре", "пять",
            "шесть", "семь", "восемь", "девять"
        };

        private static readonly string[] Teens = {
            "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать",
            "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"
        };

        private static readonly string[] Tens = {
            "", "", "двадцать", "тридцать", "сорок", "пятьдесят",
            "шестьдесят", "семьдесят", "восемьдесят", "девяносто"
        };

        private static readonly string[] Hundreds = {
            "", "сто", "двести", "триста", "четыреста", "пятьсот",
            "шестьсот", "семьсот", "восемьсот", "девятьсот"
        };

        private static readonly string[] ThousandsForms = { "тысяча", "тысячи", "тысяч" };
        private static readonly string[] MillionsForms = { "миллион", "миллиона", "миллионов" };
        private static readonly string[] BillionsForms = { "миллиард", "миллиарда", "миллиардов" };
        private static readonly string[] TrillionsForms = { "триллион", "триллиона", "триллионов" };
        private static readonly string[] QuadrillionsForms = { "квадриллион", "квадриллиона", "квадриллионов" };
        private static readonly string[] QuintillionsForms = { "квинтиллион", "квинтиллиона", "квинтиллионов" };
        private static readonly string[] RublesForms = { "рубль", "рубля", "рублей" };
        private static readonly string[] KopecksForms = { "копейка", "копейки", "копеек" };

        public static string Convert(double amount)
        {
            long rubles = (long)Math.Truncate(amount);
            int kopecks = (int)Math.Round((amount - rubles) * 100);

            if (kopecks >= 100)
            {
                rubles += kopecks / 100;
                kopecks = kopecks % 100;
            }

            string numberText = ConvertNumber(rubles);
            string rubleWord = RublesForms[GetPluralForm(rubles)];
            string kopText = kopecks.ToString("D2") + " " + KopecksForms[GetPluralForm(kopecks)];

            string result = $"{numberText} {rubleWord} {kopText}";

            // Capitalize first letter
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);

            return result;
        }

        private static string ConvertNumber(long number)
        {
            if (number == 0) return "ноль";

            string result = "";

            // Quintillions (10^18)
            if (number >= 1_000_000_000_000_000_000L)
            {
                long quintillions = number / 1_000_000_000_000_000_000L;
                result += ConvertTriplet(quintillions, false) + " " + QuintillionsForms[GetPluralForm(quintillions)] + " ";
                number %= 1_000_000_000_000_000_000L;
            }

            // Quadrillions (10^15)
            if (number >= 1_000_000_000_000_000L)
            {
                long quadrillions = number / 1_000_000_000_000_000L;
                result += ConvertTriplet(quadrillions, false) + " " + QuadrillionsForms[GetPluralForm(quadrillions)] + " ";
                number %= 1_000_000_000_000_000L;
            }

            // Trillions (10^12)
            if (number >= 1_000_000_000_000L)
            {
                long trillions = number / 1_000_000_000_000L;
                result += ConvertTriplet(trillions, false) + " " + TrillionsForms[GetPluralForm(trillions)] + " ";
                number %= 1_000_000_000_000L;
            }

            // Billions
            if (number >= 1000000000)
            {
                long billions = number / 1000000000;
                result += ConvertTriplet(billions, false) + " " + BillionsForms[GetPluralForm(billions)] + " ";
                number %= 1000000000;
            }

            // Millions
            if (number >= 1000000)
            {
                long millions = number / 1000000;
                result += ConvertTriplet(millions, false) + " " + MillionsForms[GetPluralForm(millions)] + " ";
                number %= 1000000;
            }

            // Thousands
            if (number >= 1000)
            {
                long thousands = number / 1000;
                result += ConvertTriplet(thousands, true) + " " + ThousandsForms[GetPluralForm(thousands)] + " ";
                number %= 1000;
            }

            // Remainder 1-999
            if (number > 0)
            {
                result += ConvertTriplet(number, false) + " ";
            }

            return result.Trim();
        }

        private static string ConvertTriplet(long number, bool feminine)
        {
            if (number < 0) number = -number;
            string result = "";

            int hundreds = (int)(number / 100);
            int remainder = (int)(number % 100);

            if (hundreds > 0)
                result += Hundreds[hundreds] + " ";

            if (remainder == 0)
                return result.Trim();

            if (remainder < 10)
            {
                result += (feminine ? OnesFem[remainder] : Ones[remainder]);
            }
            else if (remainder < 20)
            {
                result += Teens[remainder - 10];
            }
            else
            {
                int tens = remainder / 10;
                int ones = remainder % 10;
                result += Tens[tens];
                if (ones > 0)
                    result += " " + (feminine ? OnesFem[ones] : Ones[ones]);
            }

            return result;
        }

        private static int GetPluralForm(long number)
        {
            long abs = Math.Abs(number);
            int lastTwo = (int)(abs % 100);
            int lastOne = (int)(abs % 10);

            if (lastTwo >= 11 && lastTwo <= 19) return 2;
            if (lastOne == 1) return 0;
            if (lastOne >= 2 && lastOne <= 4) return 1;
            return 2;
        }
    }
}
