using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Web.Deployment;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var destination = new DeploymentBaseOptions { IncludeAcls = false };
            string target;
            if (args.Length == 2 && args[0] == "--local-test")
            {
                target = Path.Combine(Path.GetFullPath(args[1]), "app_offline.htm");
                if (!File.Exists(target)) throw new InvalidOperationException("The local offline test file is missing.");
            }
            else
            {
                var site = Environment.GetEnvironmentVariable("BP_MAINTENANCE_SITE") ?? "";
                if (!Regex.IsMatch(site, @"^[A-Za-z0-9_-]+(/[A-Za-z0-9_.-]+)*$") || Array.IndexOf(site.Split('/'), "..") >= 0)
                    throw new InvalidOperationException("Invalid Web Deploy site.");
                target = site + "/app_offline.htm";
                destination.ComputerName = Environment.GetEnvironmentVariable("BP_MAINTENANCE_URL");
                destination.UserName = Environment.GetEnvironmentVariable("BP_MAINTENANCE_USER");
                destination.Password = Environment.GetEnvironmentVariable("BP_MAINTENANCE_PASSWORD");
                destination.AuthenticationType = "Basic";
            }
            var sourceFile = Environment.GetEnvironmentVariable("BP_MAINTENANCE_SOURCE");
            if (!File.Exists(sourceFile)) throw new InvalidOperationException("The offline source file is missing.");
            using (var source = DeploymentManager.CreateObject("contentPath", sourceFile))
            {
                var sync = new DeploymentSyncOptions { DeleteDestination = true };
                sync.Rules.Remove("BackupRule");
                source.SyncTo("contentPath", target, destination, sync);
            }
            Console.WriteLine("Removed app_offline.htm after successful deployment.");
            return 0;
        }
        catch (Exception)
        {
            // Do not log connection objects or credentials.
            Console.Error.WriteLine("The maintenance page could not be removed. Republish successfully or remove only app_offline.htm from the website root after checking deployment.");
            return 1;
        }
    }
}
