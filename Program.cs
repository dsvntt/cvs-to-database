using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using System.Data.SqlClient;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Validators;

//Laitan pienet kommentit tänne vähän joka suuntaan niin kaikkien on kivempaa tutkia koodia ja itelle jää mieleen nämä kaikki paremmin 

namespace CsvDataProcessing
{
    public class LocationData
    {
        public int Tunniste { get; set; }
        public int? Parent { get; set; }
        public string? Nimi { get; set; }
        public string? Tyyppi { get; set; }
        public string? Vessa { get; set; }
        public int? IVTuki { get; set; }
        public int? Sahkoinen { get; set; } // Ihan alussa määrittelin luokan jossa on kaikki data.csv:n tiedoston nimikkeet
    }

    public class LocationDataMap : ClassMap<LocationData>
    {
        public LocationDataMap()
        {
            Map(m => m.Tunniste).Name("Tunniste");
            Map(m => m.Parent).Name("Parent");
            Map(m => m.Nimi).Name("Nimi");
            Map(m => m.Tyyppi).Name("Tyyppi");
            Map(m => m.Vessa).Name("Vessa");
            Map(m => m.IVTuki).Name("IV-tuki");
            Map(m => m.Sahkoinen).Name("Sähköinen"); 
            // Sen jälkeen käyttämällä Map-funktiota asensin 
            // jokaiselle nimikkeelle oman nimen. Päätös tehdä näin tuli koska muuttujissa 
            // ei voi olla "-"-merkkejä (IV-tuki) eikä "äö"-merkkejä (Sähköinen). 
            // Ilman näitä oli hankalaa hakea tietoa .csv tiedostosta.
        }
    }

    class Program
    {
        private static Timer _debounceTimer; // Otetaan käyttöön ajastin .csv-tiedoston muokkaamista varten. Ei oo mitään järkeä ilmoittaa jokaisen painalluksen jälkeen että tiedostoa on muokattu, tämäm ajastimen ansiosta ilmoitus tulee lopussa vain kerran!
        private static string _sourceFilePath; 
        private static string _connectionString;

        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true); 

            IConfigurationRoot configuration = builder.Build(); // Kaikki -ei tähän kuuluvat- koodipätkät (mm. Microsoft Azure Connection String) on siiretty appsettings.json-konffifiluun. Tässä tapahtuu yhdistäminen kyseiseen tiedostoon

            _sourceFilePath = configuration["FilePaths:SourceFilePath"]; // _sourceFilePath hakee tiedostopolun data.csv-tiedostoon konffifilusta
            _connectionString = configuration.GetConnectionString("DefaultConnection"); // _connectionString hakee Microsoft Azure Connection String:in konffifilusta

            var records = ReadCsvFile(_sourceFilePath); // ReadCsvFile = funktio joka lukee .csv tiedoston ja asentaa tekstiversion records-muuttujaan
            WriteToDatabase(records, _connectionString); // WriteToDatabase = funktio, joka ottaa .csv-tekstiversion records-muuttujasta ja lähettää sen tietokantaan

            using (var watcher = new FileSystemWatcher()) // Tämä pätkä on vastuussa .csv-tiedoston seuraamisesta. Heti kun .csv-tiedosto muuttuu -> kutstaan OnFileChanged funktio paikalle!
            {
                watcher.Path = Path.GetDirectoryName(Path.GetFullPath(_sourceFilePath)); 
                watcher.Filter = Path.GetFileName(_sourceFilePath); // Etsitään oikea tiedosto mitä pitää tarkkailla
                watcher.Changed += OnFileChanged; // Joku muutos tiedostossa? Heti kutsutaan OnFileChanged

                watcher.EnableRaisingEvents = true; // Laitetaan päälle tarkkailu.

                Console.WriteLine("Haluatko jo lähteä?:( Paina q jos näin on"); 
                while (Console.ReadKey(intercept: true).Key != ConsoleKey.Q) ;
                Console.WriteLine("Heippa!"); // Pienet hyvästelyt käyttäjän kanssa
            }
        }

        private static void OnFileChanged(object source, FileSystemEventArgs e) // Tässä on se suosittu funktio joka kutsutaan aina kun tiedosto muuttuu
        {
            Console.WriteLine($"Taisit äksen muokata seuraavan tiedoston: {e.Name}");

            _debounceTimer?.Dispose(); // Tyhjentää ajastimen, jos se on jo olemassa, jotta ei aiheudu moninkertaisia ilmoituksia

            _debounceTimer = new Timer(UpdateFile, null, 1000, Timeout.Infinite); // Asettaa uuden ajastimen, joka kutsuu UpdateFile-funktiota sekunnin kuluttua. Tämä varmistaa, että ilmoitus tulee vain kerran, vaikka tiedostoa muokattaan monesti
        }

        private static void UpdateFile(object state)
        {
            var updatedRecords = ReadCsvFile(_sourceFilePath); // Lukee uudet tiedot .csv-tiedostosta käyttämällä ReadCsvFile-funktiota
            WriteToDatabase(updatedRecords, _connectionString); // Kirjoittaa päivitetyt tiedot tietokantaan
        }

        public static List<LocationData> ReadCsvFile(string filePath) // Tämä funktio vastaa .csv-tiedoston lukemisesta
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) // Tässä koodipätkässä määritellään miten CsvHelper tulee toimimaan
            {
                Delimiter = ";", // Määritellään tiedoston kenttien erotinmerkiksi puolipiste
                HeaderValidated = null, // Ei tule otsikon validointia
                MissingFieldFound = null, // Älä yee mitään jos kenttä puttuu
                ReadingExceptionOccurred = context => false // Ohittaa lukuvirheet ja jatkaa lukemista
            };

            using (var reader = new StreamReader(filePath)) // Avaa tiedoston lukemista varten
            using (var csv = new CsvReader(reader, config)) // Luo CsvReader-olion meidän konfiguraatiolla
            {
                csv.Context.RegisterClassMap<LocationDataMap>(); // Rekisteröi kartan, joka määrittelee, miten tiedot luetaan .csv-tiedostosta

                var records = csv.GetRecords<LocationData>().ToList(); // Lukee tiedot .csv-tiedostosta listaan
                records = LocationDataValidator.Validate(records); // Tarkistetaan kaikki (ainakin yritin keksiä kaikki mahdolliset tarkistukset) mahdolliset virheet. Tämä tapahtuu toisessa tiedostossa (LocationDataValidator)

                return records; // Palauttaa luetut ja tarkistetut tiedot
            }
        }

        private static Dictionary<string, string> GetFieldNames()
        {
            var map = new LocationDataMap(); // Luo uuden kartan, joka määrittelee, miten LocationData-olio on kuvattu
            return typeof(LocationData).GetProperties().ToDictionary(
                prop => prop.Name, // Avain sanakirjaan on ominaisuuden nimi luokasta LocationData
                prop => map.MemberMaps.FirstOrDefault(m => m.Data.Member.Name == prop.Name)?.Data.Names.FirstOrDefault() // Arvo sanakirjaan on ensimmäinen nimi, joka vastaa ominaisuuden nimeä kartassa
            );
        }

        public static string GetRecordOutput(Dictionary<string, string> fieldNames, LocationData record) // Funktio, joka tulostaa konssoliin .csv- tiedoston tiedot. Siiretty omaan funktioon koodin ymmärtämisen helpottamiseksi varten. 
        {
            return $"Seuraavat tiedot: {fieldNames["Tunniste"]} = {record.Tunniste}, " +
                   $"{fieldNames["Parent"]} = {record.Parent?.ToString() ?? "null"}, " +
                   $"{fieldNames["Nimi"]} = {record.Nimi ?? "null"}, " +
                   $"{fieldNames["Tyyppi"]} = {record.Tyyppi ?? "null"}, " +
                   $"{fieldNames["Vessa"]} = {(string.IsNullOrEmpty(record.Vessa) ? "null" : record.Vessa)}, " +
                   $"{fieldNames["IVTuki"]} = {record.IVTuki?.ToString() ?? "null"}, " +
                   $"{fieldNames["Sahkoinen"]} = {record.Sahkoinen?.ToString() ?? "null"}"; // Jos joku kentistä (paitsi Tunniste) on tyhjä niin siihen lisätään "null"
        }

        public static void WriteToDatabase(List<LocationData> records, string connectionString) // Tämä funktio kirjoittaa uudet tiedot tietokantaan
        {
            var fieldNames = GetFieldNames(); // Haetaan kenttien nimet

            foreach (var record in records)
            {
                Console.WriteLine(GetRecordOutput(fieldNames, record)); // Kutsutaan GetRecordOutput-funktio joka tulostaa konssoliin csv-tiedoston tiedot
            }

            Console.WriteLine("Pitäiskö noi kaikki tallentaa tietokantaan? Kirjoita vaikka juu niin eiköhä se hoidu"); // Kysymys käyttäjälle
            if (Console.ReadLine() != "juu") // Jos käyttäjä vastaa "juu" niin viedään tiedot tietokantaan, muuten ohjelman toteutus pysähtyy
            {
                Console.WriteLine("Eipä sitte");
                return;
            }

            using (var connection = new SqlConnection(connectionString)) // Yhteys tietokantaan
            {
                connection.Open(); // Avataan yhteys

                foreach (var record in records)
                {
                    var checkCommand = new SqlCommand("SELECT COUNT(*) FROM LocationData WHERE Tunniste = @Tunniste", connection); // Tarkistetaan onko kyseisen record:in tiedot jo olemassa
                    checkCommand.Parameters.AddWithValue("@Tunniste", record.Tunniste); 
                    var exists = (int)checkCommand.ExecuteScalar() > 0;

                    var commandText = exists ? // Jos tiedot ovat olemassa -> päivitä. Muuten lisää uusi
                        @"UPDATE LocationData SET Parent = @Parent, Nimi = @Nimi, Tyyppi = @Tyyppi, Vessa = @Vessa, IVTuki = @IVTuki, Sahkoinen = @Sahkoinen WHERE Tunniste = @Tunniste" :
                        @"INSERT INTO LocationData (Tunniste, Parent, Nimi, Tyyppi, Vessa, IVTuki, Sahkoinen) VALUES (@Tunniste, @Parent, @Nimi, @Tyyppi, @Vessa, @IVTuki, @Sahkoinen)";

                    var command = new SqlCommand(commandText, connection); // Luodaan command jossa on joko "INSERT" tai "UPDATE" sekä yhtetys tietokantaan
                    
                    // Lisää parametrit komentoon
                    command.Parameters.AddWithValue("@Tunniste", record.Tunniste);
                    command.Parameters.AddWithValue("@Parent", (object)record.Parent ?? DBNull.Value); // Jos tyhjä -> Lisää "null"
                    command.Parameters.AddWithValue("@Nimi", record.Nimi);
                    command.Parameters.AddWithValue("@Tyyppi", record.Tyyppi);
                    command.Parameters.AddWithValue("@Vessa", record.Vessa ?? ""); 
                    command.Parameters.AddWithValue("@IVTuki", (object)record.IVTuki ?? DBNull.Value); // Jos tyhjä -> Lisää "null"
                    command.Parameters.AddWithValue("@Sahkoinen", (object)record.Sahkoinen ?? DBNull.Value); // Jos tyhjä -> Lisää "null"

                    command.ExecuteNonQuery(); // Suorita kysely

                    Console.WriteLine($"Lisätään: {commandText}");
                }
                Console.WriteLine($"No sehä hoitui nopeasti, niin kuin lupasinkin!");
            }
        }

    }
}