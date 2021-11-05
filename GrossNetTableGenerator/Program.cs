using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GrossNet
{
    class Program
    {
        const string OUT_FILE = "RESULT.csv";
        const char CSV_SEPERATOR = ',';

        const double MONTHLY_GROSS_START = 1000;
        const double MONTHLY_GROSS_INCREMENT = 100;
        const double MONTHLY_GROSS_END = 10000;


        const double SV_SHARE_NORMAL = 0.1812;
        const double SV_SHARE_SPECIAL = 0.1712;
        const double SV_MAX_BASIS = 5500;

        record TaxBracket(double End, double Share);
        static readonly TaxBracket[] TAX_BRACKETS_NORMAL = new TaxBracket[]
        {
            new(    11000, 0.00),
            new(    18000, 0.20),
            new(    31000, 0.35),
            new(    60000, 0.42),
            new(    90000, 0.48),
            new(  1000000, 0.50),
            new(999999999, 0.55),
        };
        static readonly TaxBracket[] TAX_BRACKETS_SPECIAL = new TaxBracket[]
        {
            new(      620, 0.00),
            new(    25000, 0.06),
            new(    50000, 0.27),
            new(    83333, 0.3575),
            new(999999999, 0.55),
        };

        static void Main()
        {
            var infos = new List<GrossNetDeltaInfo>();
            for(var monthlyGross = MONTHLY_GROSS_START; monthlyGross < MONTHLY_GROSS_END; monthlyGross += MONTHLY_GROSS_INCREMENT)
            {
                var grossNetInfo = GetGrossNetInfo(monthlyGross);
                var prevGrossNetInfo = GetGrossNetInfo(monthlyGross - MONTHLY_GROSS_INCREMENT);

                var grossNetDeltaInfo = GetGrossNetDeltaInfo(grossNetInfo, prevGrossNetInfo);

                infos.Add(grossNetDeltaInfo);
            }

            GenerateCsv(infos);
            GenerateMarkdown(infos);
        }
        record GrossNetDeltaInfo(GrossNetInfo GrossNetInfo, double Percent, double YearlyIncrementPercent, double MonthlyIncrementPercent, double SpecialIncrementPercent);
        record GrossNetInfo(double YearlyGross, double YearlyNet, double MonthlyGross, double MonthlyNet, double SpecialNet, double MonthlySv, double SpecialSv, double MonthlyTax, double SpecialTax);
        
        static GrossNetDeltaInfo GetGrossNetDeltaInfo(GrossNetInfo grossNetInfo, GrossNetInfo prevGrossNetInfo)
        {
            var percent = grossNetInfo.YearlyNet * 100 / grossNetInfo.YearlyGross;
            // how much of the last increment do you get in a year
            var yearlyGrossDelta = grossNetInfo.YearlyGross - prevGrossNetInfo.YearlyGross;
            var yearlyNetDelta = grossNetInfo.YearlyNet - prevGrossNetInfo.YearlyNet;
            var yearlyIncremenetPercent = yearlyNetDelta * 100 / yearlyGrossDelta;
            // how much of the last increment do you get per month (excluding special)
            var monthlyGrossDelta = grossNetInfo.MonthlyGross - prevGrossNetInfo.MonthlyGross;
            var monthlyNetDelta = grossNetInfo.MonthlyNet - prevGrossNetInfo.MonthlyNet;
            var monthlyIncremenetPercent = monthlyNetDelta * 100 / monthlyGrossDelta;
            // how much of the last increment do you get per special
            var specialNetDelta = grossNetInfo.SpecialNet - prevGrossNetInfo.SpecialNet;
            var specialIncremenetPercent = specialNetDelta * 100 / monthlyGrossDelta;

            return new(grossNetInfo, percent, yearlyIncremenetPercent, monthlyIncremenetPercent, specialIncremenetPercent);
        }
        static GrossNetInfo GetGrossNetInfo(double monthlyGross)
        {
            if (monthlyGross < 500) throw new Exception("marginalization limit not implemented");
            var yearlyGross = monthlyGross * 14;

            // normal sv
            var monthlyNormalSv = Math.Min(SV_MAX_BASIS, monthlyGross) * SV_SHARE_NORMAL;
            var yearlyNormalSv = monthlyNormalSv * 12;

            // special sv
            var specialSv = Math.Min(SV_MAX_BASIS, monthlyGross) * SV_SHARE_SPECIAL;
            var yearlySpecialSv = specialSv * 2;

            // normal salary tax
            var yearlyNormalTaxBasis = monthlyGross * 12 - yearlyNormalSv;
            var yearlyNormalTax = GetTax(yearlyNormalTaxBasis, TAX_BRACKETS_NORMAL);
            var monthlyNet = (monthlyGross * 12 - yearlyNormalSv - yearlyNormalTax) / 12;
            var monthlyNormalTax = yearlyNormalTax / 12;

            // special salary tax
            var yearlySpecialTaxBasis = monthlyGross * 2 - yearlySpecialSv;
            var yearlySpeicalTax = GetTax(yearlySpecialTaxBasis, TAX_BRACKETS_SPECIAL);
            var specialNet = (monthlyGross * 2 - yearlySpecialSv - yearlySpeicalTax) / 2;
            var monthlySpecialTax = yearlySpeicalTax / 2;


            var yearlyNet = monthlyGross * 14 - yearlyNormalSv - yearlySpecialSv - yearlyNormalTax - yearlySpeicalTax;

            return new(
                yearlyGross, 
                yearlyNet, 
                monthlyGross, 
                monthlyNet, 
                specialNet, 
                monthlyNormalSv,
                specialSv,
                monthlyNormalTax,
                monthlySpecialTax);
        }

        static double GetTax(double basis, TaxBracket[] taxBrackets)
        {
            var tax = 0.0;
            var bracketStart = 0.0;
            foreach(var bracket in taxBrackets)
            {
                if (basis < bracketStart) break;

                var bracketRange = bracket.End - bracketStart;
                var valueInBracket = Math.Min(bracketRange, basis - bracketStart);

                tax += valueInBracket * bracket.Share;
                bracketStart = bracket.End;
            }
            return tax;
        }

        static void GenerateCsv(List<GrossNetDeltaInfo> infos)
        {
            var outputSb = new StringBuilder();
            outputSb.AppendLine(string.Join(CSV_SEPERATOR, new[]
            {
                "YearlyGross",
                "YearlyNet",
                "YearlyNet/12",
                "MonthlyGross",
                "MonthlyNet",
                "SpecialNet",
                "TotalPercent",
                "YearlyIncrement[%]",
                "MonthlyIncrement[%]",
                "SpecialIncrement[%]",
                "MonthlySv",
                "SpecialSv",
                "MonthlyTax",
                "SpecialTax",
            }));
            foreach(var info in infos)
            {
                outputSb.AppendLine(string.Join(CSV_SEPERATOR, new[]
                {
                    (int)Math.Round(info.GrossNetInfo.YearlyGross),
                    (int)Math.Round(info.GrossNetInfo.YearlyNet),
                    (int)Math.Round(info.GrossNetInfo.YearlyNet/12),
                    (int)Math.Round(info.GrossNetInfo.MonthlyGross),
                    (int)Math.Round(info.GrossNetInfo.MonthlyNet),
                    (int)Math.Round(info.GrossNetInfo.SpecialNet),
                    (int)Math.Round(info.Percent),
                    (int)Math.Round(info.YearlyIncrementPercent),
                    (int)Math.Round(info.MonthlyIncrementPercent),
                    (int)Math.Round(info.SpecialIncrementPercent),
                    (int)Math.Round(info.GrossNetInfo.MonthlySv),
                    (int)Math.Round(info.GrossNetInfo.SpecialSv),
                    (int)Math.Round(info.GrossNetInfo.MonthlyTax),
                    (int)Math.Round(info.GrossNetInfo.SpecialTax),
                }));
            }

            Console.Write(outputSb);
            File.WriteAllText(OUT_FILE, outputSb.ToString());
        }
        static void GenerateMarkdown(List<GrossNetDeltaInfo> infos)
        {
            var outputSb = new StringBuilder();
            var columns = new[]
            {
                "MonthlyGross",
                "MonthlyNet",
                "SpecialNet",
                "YearlyNet/12",
                "Total[%]",
                "YearlyIncrement[%]",
            };
            for (var i = 0; i < infos.Count; i++)
            {
                // make table easier readable by splitting it per 10 entries
                if (i % 10 == 0)
                {
                    outputSb.AppendLine();
                    outputSb.AppendLine(string.Join('|', columns));
                    outputSb.AppendLine(string.Join('|', columns.Select(_ => '-')));
                }

                // write infos
                var info = infos[i];
                outputSb.AppendLine(string.Join('|', new[]
                {
                    (int)Math.Round(info.GrossNetInfo.MonthlyGross),
                    (int)Math.Round(info.GrossNetInfo.MonthlyNet),
                    (int)Math.Round(info.GrossNetInfo.SpecialNet),
                    (int)Math.Round(info.GrossNetInfo.YearlyNet/12),
                    (int)Math.Round(info.Percent),
                    (int)Math.Round(info.YearlyIncrementPercent),
                }));
            }

            var template = File.ReadAllText("README_TEMPLATE.md");
            var markdown = template.Replace("<table/>", outputSb.ToString());
            File.WriteAllText("README.md", markdown);
        }
    }
}
