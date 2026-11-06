namespace Net7ClientManager.Tests;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Net7ClientManager.Contributions;
using Net7ClientManager.Models;
using Net7ClientManager.Forms;
using Net7ClientManager.Net7Site;
using Xunit;

public sealed class ForgeNet7IdentityTests
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    private const string Code="N7F-"+"1234567890123456789012345678901234567890123456789012345678901234";
    [Theory] [InlineData("create")] [InlineData("recover")] [InlineData("link")]
    public async Task SavesIdentityButNeverSessionOrRecoverySecret(string action) {
        var settings=new ForgeContributionSettings(); var saves=0; var calls=0;
        using var client=new ForgeContributionClient(new Handler(async r=> {
            calls++; Assert.Equal("net7forge.com",r.RequestUri!.Host);
            if(r.RequestUri.AbsolutePath.EndsWith("challenge",StringComparison.Ordinal)) return Reply(new Net7IdentityChallenge(new string('A',64),DateTimeOffset.UtcNow.AddMinutes(5)));
            var request=JsonSerializer.Deserialize<Net7IdentityRequest>(await r.Content!.ReadAsStringAsync(),Options)!;
            Assert.Equal(action,request.Action); Assert.Equal("synthetic-session",request.SessionCookie); Assert.Equal(7,request.AccountId);
            Assert.Equal(action=="recover"?Code:null,request.RecoveryCode);
            using var key=ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.PublicKey),out _);
            Assert.True(key.VerifyData(Net7IdentityCanonicalizer.Canonicalize(request),Convert.FromBase64String(request.Signature),HashAlgorithmName.SHA256));
            return Reply(new Net7IdentityResponse {ContributorId="contrib_test",AccountId=7,RecoveryCode=Code,RegisteredAtUtc=DateTimeOffset.UtcNow});
        }));
        var service=new ForgeContributionIdentityService(client,settings,()=>saves++);
        var response=await service.VerifyNet7IdentityAsync(action,7,"synthetic-session",action=="recover"?"  "+Code.ToLowerInvariant()+"  ":null,default);
        Assert.Equal(Code,response.RecoveryCode); Assert.Equal("contrib_test",settings.ContributorId); Assert.Equal(7,settings.Net7RecoveryAccountId);
        Assert.Equal(2,calls); Assert.True(saves>=2); Assert.NotNull(service.TryGetExistingIdentity());
        var saved=JsonSerializer.Serialize(settings); Assert.DoesNotContain(Code,saved); Assert.DoesNotContain("synthetic-session",saved);
    }
    [Fact] public async Task NewInstallRequiresExplicitEnrollmentWithoutNetworkPublishing() {
        using var client=new ForgeContributionClient(new Handler(r=> { Assert.EndsWith("/net7/capabilities",r.RequestUri!.AbsolutePath); return Task.FromResult(Reply(new { protocolVersion=1 })); }), new Handler(_=>throw new InvalidOperationException("Unexpected publishing request")));
        var service=new ForgeContributionIdentityService(client,new(),()=>{});
        await Assert.ThrowsAsync<ForgeIdentityRequiredException>(()=>service.EnsureAsync("Pilot",default));
    }
    [Fact] public async Task NewClientUsesLegacyRegistrationOnOlderForge() {
        var registrations = 0; var capabilityChecks = 0;
        using var client = new ForgeContributionClient(
            new Handler(r => { capabilityChecks++; Assert.EndsWith("/net7/capabilities", r.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }),
            new Handler(async r => {
                Assert.EndsWith("/contributors/register", r.RequestUri!.AbsolutePath);
                var request = JsonSerializer.Deserialize<ForgeContributorRegistrationRequest>(await r.Content!.ReadAsStringAsync(), Options)!;
                using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.PublicKey), out _);
                Assert.True(key.VerifyData(ForgeContributionCanonicalizer.Canonicalize(request), Convert.FromBase64String(request.Signature), HashAlgorithmName.SHA256));
                registrations++; return Reply(new { contributorId="legacy-id", registeredAtUtc=DateTimeOffset.UtcNow });
            }));
        var settings = new ForgeContributionSettings();
        var service = new ForgeContributionIdentityService(client,settings,()=>{});
        Assert.Equal("legacy-id", (await service.EnsureAsync("Pilot",default)).ContributorId);
        Assert.Equal("legacy-id", (await service.EnsureAsync("Pilot",default)).ContributorId);
        Assert.Equal("legacy-id", (await service.EnsureAsync("AnotherPilot",default)).ContributorId);
        Assert.Equal(2, registrations); Assert.Equal(1, capabilityChecks); Assert.Null(settings.Net7RecoveryAccountId);
    }
    [Theory] [InlineData(500)] [InlineData(403)] [InlineData(429)] [InlineData(200)]
    public async Task CapabilityFailureDoesNotSilentlyFallBack(int status) {
        var legacyCalls = 0;
        using var client = new ForgeContributionClient(new Handler(_=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content=new StringContent("invalid") })),new Handler(_=> { legacyCalls++; throw new InvalidOperationException("Unexpected legacy registration"); }));
        var settings = new ForgeContributionSettings();
        var service = new ForgeContributionIdentityService(client,settings,()=>{});
        await Assert.ThrowsAnyAsync<Exception>(()=>service.EnsureAsync("Pilot",default));
        Assert.Null(settings.ContributorId); Assert.Equal(0, legacyCalls);
    }
    [Fact] public async Task MalformedServerReplyIsHandledAsIdentityError() {
        using var client=new ForgeContributionClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("not json")})));
        await Assert.ThrowsAsync<ForgeIdentityException>(()=>client.GetNet7IdentityChallengeAsync(default));
    }
    [Fact] public async Task RejectedVerificationDoesNotEnroll() {
        using var client=new ForgeContributionClient(new Handler(r=>Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("challenge",StringComparison.Ordinal)?Reply(new Net7IdentityChallenge(new string('A',64),DateTimeOffset.UtcNow.AddMinutes(5))):new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("""{"error":"recovery_verification_failed","message":"No match"}""")})));
        var settings=new ForgeContributionSettings(); var service=new ForgeContributionIdentityService(client,settings,()=>{});
        await Assert.ThrowsAsync<ForgeIdentityApiException>(()=>service.VerifyNet7IdentityAsync("recover",7,"session",Code,default));
        Assert.Null(settings.ContributorId); Assert.Null(settings.Net7RecoveryAccountId);
    }
    [Fact] public void RecoveryCodeRequiresExplicitSavedAcknowledgement() {
        OnSta(()=> { using var form=new ForgeRecoveryCodeForm(Code,"Example account"); form.CreateControl();
            var controls=Descendants(form).ToArray(); var done=controls.OfType<Button>().Single(b=>b.Text=="Done");
            Assert.False(done.Enabled); controls.OfType<CheckBox>().Single().Checked=true; Assert.True(done.Enabled);
            Assert.Equal(Code,controls.OfType<TextBox>().Single().Text);
            var output=Environment.GetEnvironmentVariable("FORGE_TEST_SCREENSHOTS");
            if(output!=null) { Directory.CreateDirectory(output); form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-10000,-10000); form.Show(); Application.DoEvents(); form.PerformLayout(); using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"recovery-code.png")); }
        });
    }
    [Fact] public void EnrollmentStartsDisabledAndOffersRecovery() {
        OnSta(()=> { var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
            using var form=new ForgeNet7IdentityForm(false,(_,_,_,_,_)=>throw new InvalidOperationException("No submit"),new Net7SiteSessionStore(path));
            form.CreateControl(); var controls=Descendants(form).ToArray(); Assert.False(controls.OfType<Button>().Single(b=>b.Text=="Verify and connect").Enabled);
            Assert.Contains(controls.OfType<ComboBox>(),b=>b.Items.Contains("Recover using saved code"));
            var output=Environment.GetEnvironmentVariable("FORGE_TEST_SCREENSHOTS");
            if(output!=null) { Directory.CreateDirectory(output); form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-10000,-10000); form.Show(); Application.DoEvents(); form.PerformLayout(); using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"enrollment.png")); }
        });
    }
    private static IEnumerable<Control> Descendants(Control c) { foreach(Control child in c.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; } }
    private static void OnSta(Action action) { Exception? error=null; var thread=new Thread(()=>{try{action();}catch(Exception e){error=e;}}); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); }
    private static HttpResponseMessage Reply<T>(T value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Options),System.Text.Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>send(r); }
}
