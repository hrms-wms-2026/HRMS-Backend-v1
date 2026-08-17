namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only: hand-designed Work Management demo dataset for the "dapi" smoke tenant
/// (owner dapiyshanth1908@gmail.com). Pure data - no DB access here. Consumed by
/// WorkManagementDapiDemoSeeder. Every DemoObjectiveNode.Children entry must satisfy
/// ObjectiveParentConstraintChecker.Conflicts by construction once dates/hours are computed by
/// the seeder's tree walk (see WorkManagementDapiDemoSeeder.ComputeChildDates/ComputeChildHours).
/// </summary>
public sealed record DemoPerson(
    string Key,
    string FirstName,
    string LastName,
    string Email,
    string EmployeeNumber,
    DateOnly HireDate);

public sealed record DemoObjectiveNode(
    string Title,
    string OwnerKey,
    string[] ExtraMemberKeys,
    DemoObjectiveNode[] Children)
{
    public DemoObjectiveNode(string title, string ownerKey, DemoObjectiveNode[] children)
        : this(title, ownerKey, [], children)
    {
    }

    public DemoObjectiveNode(string title, string ownerKey, string[] extraMemberKeys)
        : this(title, ownerKey, extraMemberKeys, [])
    {
    }

    public DemoObjectiveNode(string title, string ownerKey)
        : this(title, ownerKey, [], [])
    {
    }
}

public sealed record DemoProjectTree(
    string ProjectKey,
    string ProjectName,
    string Identifier,
    string CategoryName,
    DateOnly StartDate,
    DateOnly TargetDate,
    decimal AllocatedHours,
    DemoObjectiveNode Root);

public static class WorkManagementDapiDemoData
{
    private static readonly DateOnly EposTeamHireDate = new(2023, 3, 1);
    private static readonly DateOnly EventTeamHireDate = new(2023, 6, 1);
    private static readonly DateOnly OnexsoTeamHireDate = new(2023, 1, 10);
    private static readonly DateOnly WatercraftTeamHireDate = new(2024, 2, 1);
    private static readonly DateOnly HardwareTeamHireDate = new(2023, 9, 1);
    private static readonly DateOnly MarketingTeamHireDate = new(2024, 5, 1);

    public static readonly IReadOnlyList<DemoPerson> Persons =
    [
        // E-pos_System team
        new("mathusanth", "Mathusanth", "Kumaran", "mathusanth.kumaran@dapi.test", "DAPI-0002", EposTeamHireDate),
        new("tharmi", "Tharmi", "Rajendran", "tharmi.rajendran@dapi.test", "DAPI-0003", EposTeamHireDate),
        new("rowsas", "Rowsas", "Fernando", "rowsas.fernando@dapi.test", "DAPI-0004", EposTeamHireDate),
        new("nevi", "Nevi", "Peiris", "nevi.peiris@dapi.test", "DAPI-0005", EposTeamHireDate),

        // Event management ticketing team
        new("danuharan", "Danuharan", "Wickramasinghe", "danuharan.wickramasinghe@dapi.test", "DAPI-0006", EventTeamHireDate),
        new("thamsan", "Thamsan", "Jayasuriya", "thamsan.jayasuriya@dapi.test", "DAPI-0007", EventTeamHireDate),
        new("kali", "Kali", "Senanayake", "kali.senanayake@dapi.test", "DAPI-0008", EventTeamHireDate),
        new("thivshana", "Thivshana", "Gunawardena", "thivshana.gunawardena@dapi.test", "DAPI-0009", EventTeamHireDate),

        // Onexso (HR & Work Management) team
        new("kajaa", "Kajaa", "Tharan", "kajaa.tharan@dapi.test", "DAPI-0010", OnexsoTeamHireDate),
        new("thivan", "Thivan", "Balasubramaniam", "thivan.balasubramaniam@dapi.test", "DAPI-0011", OnexsoTeamHireDate),
        new("paramanathan", "Paramanathan", "Sivakumar", "paramanathan.sivakumar@dapi.test", "DAPI-0012", OnexsoTeamHireDate),
        new("prakirthan", "Prakirthan", "Mahendran", "prakirthan.mahendran@dapi.test", "DAPI-0013", OnexsoTeamHireDate),

        // Watercraft team
        new("abitha", "Abitha", "Devendran", "abitha.devendran@dapi.test", "DAPI-0014", WatercraftTeamHireDate),
        new("saif", "Saif", "Ahamed", "saif.ahamed@dapi.test", "DAPI-0015", WatercraftTeamHireDate),
        new("lavanya", "Lavanya", "Chandrasekaran", "lavanya.chandrasekaran@dapi.test", "DAPI-0016", WatercraftTeamHireDate),
        new("kunasika", "Kunasika", "Ratnayake", "kunasika.ratnayake@dapi.test", "DAPI-0017", WatercraftTeamHireDate),

        // Hardware integration portal team (cross-project collaborators)
        new("nilaxan", "Nilaxan", "Sritharan", "nilaxan.sritharan@dapi.test", "DAPI-0018", HardwareTeamHireDate),
        new("kiru", "Kiru", "Balachandran", "kiru.balachandran@dapi.test", "DAPI-0019", HardwareTeamHireDate),
        new("basith", "Basith", "Ismail", "basith.ismail@dapi.test", "DAPI-0020", HardwareTeamHireDate),

        // Marketing team (standalone, cross-project collaborators)
        new("sutharshan", "Sutharshan", "Nadarajah", "sutharshan.nadarajah@dapi.test", "DAPI-0021", MarketingTeamHireDate),
        new("kavisna", "Kavisna", "Rajapaksa", "kavisna.rajapaksa@dapi.test", "DAPI-0022", MarketingTeamHireDate),
        new("sangavi", "Sangavi", "Thavarajah", "sangavi.thavarajah@dapi.test", "DAPI-0023", MarketingTeamHireDate),
    ];

    public static readonly IReadOnlyDictionary<string, DemoPerson> PersonsByKey =
        Persons.ToDictionary(p => p.Key, p => p);

    public static readonly IReadOnlyList<string> ProjectCategoryNames =
        ["Engineering", "Product", "R&D", "Operations", "Marketing"];

    public static readonly IReadOnlyList<DemoProjectTree> ProjectTrees =
    [
        new(
            ProjectKey: "epos",
            ProjectName: "E-pos_System",
            Identifier: "EPOS",
            CategoryName: "Engineering",
            StartDate: new DateOnly(2026, 3, 1),
            TargetDate: new DateOnly(2027, 1, 31),
            AllocatedHours: 4200m,
            Root: new DemoObjectiveNode("E-pos_System", "dabi",
            [
                new DemoObjectiveNode("Pos System", "mathusanth",
                [
                    new DemoObjectiveNode("System architecture", "tharmi",
                    [
                        new DemoObjectiveNode("Frontend architecture", "mathusanth",
                        [
                            new DemoObjectiveNode("UI component library", "nevi"),
                        ]),
                        new DemoObjectiveNode("Backend architecture", "rowsas",
                        [
                            new DemoObjectiveNode("Database schema design", "nevi"),
                        ]),
                    ]),
                    new DemoObjectiveNode("System R&D", "rowsas"),
                    new DemoObjectiveNode("Non functionality", "nevi"),
                    new DemoObjectiveNode("Development plan", "rowsas"),
                ]),
                new DemoObjectiveNode("Building system", "tharmi"),
                new DemoObjectiveNode("Payment gateway", "rowsas"),
                new DemoObjectiveNode("Testing and deployment", "nevi", ["mathusanth"]),
                new DemoObjectiveNode("Hardware Integration", "nilaxan", ["kiru", "basith"]),
                new DemoObjectiveNode("Marketing", "sutharshan", ["kavisna", "sangavi"]),
            ])),

        new(
            ProjectKey: "evtix",
            ProjectName: "Event management ticketing",
            Identifier: "EVTIX",
            CategoryName: "Product",
            StartDate: new DateOnly(2026, 4, 1),
            TargetDate: new DateOnly(2026, 12, 31),
            AllocatedHours: 3200m,
            Root: new DemoObjectiveNode("Event management ticketing", "dabi",
            [
                new DemoObjectiveNode("Ticketing Platform", "danuharan",
                [
                    new DemoObjectiveNode("Booking Engine", "thamsan",
                    [
                        new DemoObjectiveNode("Seat Selection Module", "kali",
                        [
                            new DemoObjectiveNode("Seat Map Rendering", "thivshana"),
                        ]),
                        new DemoObjectiveNode("Pricing And Discount Engine", "danuharan"),
                    ]),
                    new DemoObjectiveNode("Event Discovery And Search", "kali"),
                    new DemoObjectiveNode("Check-in And QR Validation", "thivshana"),
                ]),
                new DemoObjectiveNode("Organizer Dashboard", "thamsan"),
                new DemoObjectiveNode("Notifications And Reminders", "kali"),
                new DemoObjectiveNode("Testing and deployment", "thivshana", ["danuharan"]),
                new DemoObjectiveNode("Hardware Integration", "kiru", ["nilaxan", "basith"]),
                new DemoObjectiveNode("Marketing", "kavisna", ["sutharshan", "sangavi"]),
            ])),

        new(
            ProjectKey: "onexso",
            ProjectName: "Onexso - HR and Work Management System",
            Identifier: "ONEXSO",
            CategoryName: "Product",
            StartDate: new DateOnly(2026, 1, 15),
            TargetDate: new DateOnly(2027, 6, 30),
            AllocatedHours: 5400m,
            Root: new DemoObjectiveNode("Onexso - HR and Work Management System", "dabi",
            [
                new DemoObjectiveNode("Core HR And Employee Management", "kajaa",
                [
                    new DemoObjectiveNode("Employee Lifecycle Module", "thivan",
                    [
                        new DemoObjectiveNode("Onboarding And Offboarding Workflows", "paramanathan",
                        [
                            new DemoObjectiveNode("Document Collection And Verification", "prakirthan"),
                        ]),
                        new DemoObjectiveNode("Org Structure And Position Management", "kajaa"),
                    ]),
                    new DemoObjectiveNode("Leave And Attendance Module", "paramanathan"),
                    new DemoObjectiveNode("Payroll And Compensation Module", "prakirthan"),
                ]),
                new DemoObjectiveNode("Work Management Module", "thivan"),
                new DemoObjectiveNode("Auth Security And Tenant Isolation", "kajaa"),
                new DemoObjectiveNode("Reporting And Analytics", "prakirthan"),
                new DemoObjectiveNode("Testing and deployment", "paramanathan", ["thivan"]),
                new DemoObjectiveNode("Hardware Integration", "basith", ["nilaxan", "kiru"]),
                new DemoObjectiveNode("Marketing", "sangavi", ["sutharshan", "kavisna"]),
            ])),

        new(
            ProjectKey: "watercraft",
            ProjectName: "Watercraft",
            Identifier: "WCRAFT",
            CategoryName: "R&D",
            StartDate: new DateOnly(2026, 2, 1),
            TargetDate: new DateOnly(2027, 3, 31),
            AllocatedHours: 4800m,
            Root: new DemoObjectiveNode("Watercraft", "dabi",
            [
                new DemoObjectiveNode("Hull And Vessel Design", "abitha",
                [
                    new DemoObjectiveNode("Structural Engineering", "saif",
                    [
                        new DemoObjectiveNode("Load And Stress Analysis", "lavanya",
                        [
                            new DemoObjectiveNode("Simulation And Stress Testing", "kunasika"),
                        ]),
                        new DemoObjectiveNode("Material Selection", "abitha"),
                    ]),
                    new DemoObjectiveNode("Propulsion System", "lavanya"),
                    new DemoObjectiveNode("Navigation And Control Systems", "kunasika"),
                ]),
                new DemoObjectiveNode("Manufacturing And Assembly", "saif"),
                new DemoObjectiveNode("Safety And Compliance", "abitha"),
                new DemoObjectiveNode("Testing and deployment", "kunasika", ["lavanya"]),
                new DemoObjectiveNode("Hardware Integration", "nilaxan", ["kiru", "basith"]),
                new DemoObjectiveNode("Marketing", "sutharshan", ["kavisna", "sangavi"]),
            ])),

        new(
            ProjectKey: "hwportal",
            ProjectName: "The Hardware integration portal",
            Identifier: "HWPORTAL",
            CategoryName: "Engineering",
            StartDate: new DateOnly(2026, 5, 1),
            TargetDate: new DateOnly(2026, 12, 15),
            AllocatedHours: 2600m,
            Root: new DemoObjectiveNode("The Hardware integration portal", "dabi",
            [
                new DemoObjectiveNode("Device Connectivity Framework", "nilaxan",
                [
                    new DemoObjectiveNode("Protocol Adapters", "kiru",
                    [
                        new DemoObjectiveNode("Driver Abstraction Layer", "basith",
                        [
                            new DemoObjectiveNode("Firmware Compatibility Testing", "nilaxan"),
                        ]),
                        new DemoObjectiveNode("Device Pairing And Discovery", "kiru"),
                    ]),
                    new DemoObjectiveNode("Sensor Data Pipeline", "basith"),
                    new DemoObjectiveNode("Cross Project Hardware Support Desk", "nilaxan"),
                ]),
                new DemoObjectiveNode("Portal Dashboard And Monitoring", "kiru"),
                new DemoObjectiveNode("Testing and deployment", "basith", ["nilaxan"]),
                new DemoObjectiveNode("Marketing", "kavisna", ["sutharshan", "sangavi"]),
            ])),
    ];
}
