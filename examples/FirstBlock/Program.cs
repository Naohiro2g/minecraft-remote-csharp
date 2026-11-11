using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Code2Create.MinecraftRemote.Client;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        try
        {
            string Read(string name) => Environment.GetEnvironmentVariable(name)
                ?? throw new ArgumentException("必要な環境変数がありません: " + name);
            int Number(string name) => int.Parse(Read(name), CultureInfo.InvariantCulture);
            var options = new ClientOptions(Read("MC_REMOTE_HOST"), Number("MC_REMOTE_PORT"),
                new ClientIdentity("csharp-first-block", "development"));
            int x = Number("MC_REMOTE_X"), y = Number("MC_REMOTE_Y"), z = Number("MC_REMOTE_Z");
            bool restore = args.Contains("--restore");

            await using var client = await MinecraftClient.ConnectAsync(options,
                credentialStore: new NoCredentialStore(),
                onPairing: async (prompt, ct) =>
                {
                    // pair codeは表示用。tokenやpairing_idは表示・保存しない。
                    Console.WriteLine("PAIRING_APPROVAL_REQUIRED");
                    Console.WriteLine(prompt.Command);
                    Console.WriteLine("Minecraft内で承認したら、この端末でEnterを押してください。");
                    if (await Console.In.ReadLineAsync(ct) == null) throw new OperationCanceledException();
                }, cancellationToken: shutdown.Token);

            Console.WriteLine("HELLO_PASS");
            Console.WriteLine("MC_VERSION=" + client.Hello.MinecraftVersion);
            BlockValue? previous = restore ? await client.GetBlockAsync(x, y, z, shutdown.Token) : null;
            bool writeStarted = false;
            try
            {
                writeStarted = true;
                await client.SetBlockAsync(x, y, z, new BlockSpec("minecraft:gold_block"), shutdown.Token);
                var actual = await client.GetBlockAsync(x, y, z, shutdown.Token);
                if (actual.BlockId != "minecraft:gold_block" || actual.State.Count != 0)
                    throw new InvalidOperationException("読み戻したブロックが一致しません。");
                Console.WriteLine("PULSE_PASS: hello / setBlock / getBlock");
            }
            finally
            {
                if (previous != null && writeStarted && client.IsConnected)
                {
                    // Ctrl+C後も、開いた同じ接続で明示的な片付けを試みる。自動再送ではない。
                    await client.SetBlockAsync(x, y, z, previous.ToSpec(), CancellationToken.None);
                    Console.WriteLine("RESTORE_PASS");
                }
                else if (previous != null && writeStarted)
                    Console.WriteLine("RESTORE_INCOMPLETE: 接続が閉じたため、Minecraft内で片付けてください。");
            }
            await client.CloseAsync();
            Console.WriteLine("CLOSE_PASS");
            return 0;
        }
        catch (RemoteErrorException error)
        {
            // full error dataはAPIで取得できる。作例のlogには接続先やcredentialを出さない。
            Console.Error.WriteLine("REMOTE_FAILURE: " + (error.Reason ?? "unknown_reason"));
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("LOCAL_FAILURE: " + error.GetType().Name);
            return 1;
        }
    }
}
