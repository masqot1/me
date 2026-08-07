using System.Text;
using TrueWebsiteCloner.Core;

var root=Environment.GetEnvironmentVariable("TWC_RELEASE_OPS_GATE_OUTPUT")??Path.Combine(Path.GetTempPath(),"TrueWebsiteCloner-Gate-0.17");
if(Directory.Exists(root))Directory.Delete(root,true);
Directory.CreateDirectory(root);
var project=Path.Combine(root,"source");
var output=Path.Combine(root,"release.twcrelease");
var workspace=Path.Combine(root,"workspace");
Directory.CreateDirectory(workspace);

static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
static async Task Write(string project,string relative,string content){var path=Path.Combine(project,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(path,content,new UTF8Encoding(false));}

await Write(project,"_network/session.json","{\"targetUrl\":\"http://127.0.0.1:7843/\",\"startedAtUtc\":\"2026-08-08T10:00:00Z\"}");
await Write(project,"_network/summary.json","{\"eventCount\":30,\"bodyCount\":8}");
await Write(project,"_bodies/bodies.jsonl","{}\n");
await Write(project,"_bodies/index.html","<html>FINAL RELEASE PAYLOAD</html>");
await Write(project,"offline/offline-manifest.json","{\"mappings\":[]}");
await Write(project,"offline/site/index.html","<html>FINAL RELEASE OFFLINE</html>");
await Write(project,"offline/missing-resources.json","[]");
await Write(project,"offline/recovery-report.json","{\"result\":\"PASS\",\"finalMissing\":0}");
await Write(project,"offline/completeness-report.json","{\"result\":\"PASS\",\"completenessScore\":100,\"weightedCompletenessScore\":100}");
await Write(project,"offline/dependency-graph.json","{\"nodes\":[],\"edges\":[]}");
await Write(project,"offline/verification-report.json","{\"result\":\"PASS\",\"unexpectedDivergences\":0}");
await Write(project,"offline/visual-comparison/visual-report.json","{\"result\":\"PASS\",\"mismatchPercent\":0.02,\"maxMismatchPercent\":0.15}");
await Write(project,"history/001-baseline/snapshot.json","{\"snapshotId\":\"final-release-snapshot\"}");

var readiness=await new ReleaseReadinessService().ValidateAsync(project);
Require(readiness.Ok&&readiness.Result=="READY","Source project is not READY: "+readiness.NextAction);
var seal=await new ReleaseSealService().CreateAsync(project);
Require(seal.Ok,"Seal creation failed: "+seal.Message);
var sealVerify=await new ReleaseSealService().VerifyAsync(project);
Require(sealVerify.Ok,"Source seal verification failed: "+sealVerify.Message);
var bundles=new ReleaseBundleService();
var created=await bundles.CreateAsync(project,output);
Require(created.Ok,"Release bundle creation failed: "+created.Message);
var verified=await bundles.VerifyAsync(output);
Require(verified.Ok,"Release bundle verification failed: "+verified.Message);
var imported=await bundles.ImportAsync(output,workspace,"verified-release");
Require(imported.Ok&&imported.DestinationPath is not null,"Release bundle import failed: "+imported.Message);
var importedSeal=await new ReleaseSealService().VerifyAsync(imported.DestinationPath!);
Require(importedSeal.Ok,"Imported release seal failed: "+importedSeal.Message);
var catalog=await new ProjectCatalogService().RefreshAsync(workspace);
Require(catalog.Ok&&catalog.Projects.Count==1,"Imported release was not cataloged");
Require(catalog.Projects[0].ImportIntegrityVerified,"Catalog did not detect PASS import integrity");
Require(catalog.Projects[0].Status=="Verified","Imported release was not classified Verified");
var reexport=Path.Combine(root,"reexport.twcproj");
var portable=await new WorkspacePortableOperations().ExportAsync(imported.DestinationPath!,reexport);
Require(portable.Ok,"Imported release re-export failed: "+portable.Message);
var portableVerify=await new PortableProjectPackage().VerifyAsync(reexport);
Require(portableVerify.Ok,"Re-exported portable project failed integrity verification: "+portableVerify.Message);

Console.WriteLine("PASS  READY source project");
Console.WriteLine("PASS  immutable release seal create/verify");
Console.WriteLine("PASS  deterministic release bundle create/verify");
Console.WriteLine("PASS  verified release bundle import");
Console.WriteLine("PASS  imported embedded seal verifies");
Console.WriteLine("PASS  imported release appears Verified in workspace catalog");
Console.WriteLine("PASS  imported project re-exports as valid V0.11 portable package");
Console.WriteLine("RESULT: GATE 0.17 PASS");
