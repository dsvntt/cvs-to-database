using CsvDataProcessing;
using System.Collections.Generic;

// Tässä tapahtuu kaikki tarkistukset

namespace Validators
{
    public static class LocationDataValidator
    {
        public static List<LocationData> Validate(List<LocationData> records)
        {
            var identifiers = new HashSet<int>(); // Kokoelma yksilöllisistä tunnisteista
            var invalidRecords = new List<LocationData>(); // Lista virheellisistä tietueista

            foreach (var record in records) // Käydään läpi kaikki tietueet
            {
                if (identifiers.Contains(record.Tunniste)) // Tarkistetaan onko tunniste jo olemassa
                {
                    Console.WriteLine($"Taisit vahingossa (tai luultavasti ihan tahallaan jos testaat tätä) kirjoittaa 2 samanlaista tunnistetta: {record.Tunniste}");
                    invalidRecords.Add(record);
                    continue;
                }

                identifiers.Add(record.Tunniste);

                if (string.IsNullOrEmpty(record.Nimi) || string.IsNullOrEmpty(record.Tyyppi)) // Tarkistetaan, että Nimi ja Tyyppi eivät ole tyhjiä
                {
                    Console.WriteLine($"Tietueessa numero {record.Tunniste} taitaa puuttua tärkeä tieto (Nimi tai Tyyppi)");
                    invalidRecords.Add(record);
                    continue;
                }

                if (record.Tunniste <= 0) // Tarkistetaan, että tunniste on suurempi kuin 0
                {
                    Console.WriteLine($"Ei tuollaista ({record.Tunniste}) tunnistetta saa käyttää!!");
                    invalidRecords.Add(record);
                    continue;
                }

                if (record.Parent != null) // Tarkistetaan Parent-kentän arvo
                {
                    if (record.Parent.Value <= 0 || record.Parent.Value >= record.Tunniste || !identifiers.Contains(record.Parent.Value))
                    {
                        Console.WriteLine($"Kyseinen Parent:in numero taitaa olla vähän pielessä: {record.Parent} (Tunniste: {record.Tunniste})");
                        invalidRecords.Add(record);
                        continue;
                    }
                }

                if (record.Vessa != null && record.Vessa != "Kyllä" && record.Vessa != "Ei" && record.Vessa != "") // Tarkistetaan Vessa-kentän arvo
                {
                    Console.WriteLine($"Vessa voi joko olla tai ei, tuollaisia vaihtoehtoja ei tietokanta hyväksy: {record.Vessa}");
                    invalidRecords.Add(record);
                    continue;
                }

                if (record.IVTuki != null && (record.IVTuki < 0 || record.IVTuki > 1)) // Tarkistetaan IVTuki-kentän arvo
                {
                    Console.WriteLine($"(Tunniste: {record.Tunniste}): Kyseinen IV-Tuki kenttä on nirso ja hyväksyy vain numerot 1 tai 0, syötit: {record.IVTuki}");
                    invalidRecords.Add(record);
                    continue;
                }

                if (record.Sahkoinen != null && (record.Sahkoinen < 0 || record.Sahkoinen > 1)) // Tarkistetaan Sähköinen-kentän arvo
                {
                    Console.WriteLine($"(Tunniste: {record.Tunniste}): Kyseinen Sähköinen kenttä on nirso ja hyväksyy vain numerot 1 tai 0, syötit: {record.Sahkoinen}");
                    invalidRecords.Add(record);
                    continue;
                }
            }

            foreach (var invalidRecord in invalidRecords) 
            {
                records.Remove(invalidRecord); // Poistetaan virheelliset tietueet
            }

            return records; // Palautetaan tarkistetut tietueet
        }
    }
}