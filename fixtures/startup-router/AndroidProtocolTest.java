package com.codephilosophy.hybridclr.startup;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.Arrays;
public final class AndroidProtocolTest {
    private static void require(boolean condition) { if (!condition) throw new AssertionError(); }
    public static void main(String[] args) throws Exception {
        byte[] scope = MessageDigest.getInstance("SHA-256").digest("protocol-vector".getBytes(java.nio.charset.StandardCharsets.UTF_8));
        byte[] nativeRecord = Files.readAllBytes(Paths.get(args[0]));
        StartupSelectionStore.Selection selection = StartupSelectionStore.decodeRecord(scope, nativeRecord);
        require(selection.requestedMode == 2 && selection.generation == 1);
        byte[] javaRecord = StartupSelectionStore.encodeRecord(scope, 2, 1);
        require(Arrays.equals(nativeRecord, javaRecord));
        Files.write(Paths.get(args[1]), javaRecord);
        byte[] tombstone = StartupSelectionStore.encodeRecord(scope, 0, 2);
        require(StartupSelectionStore.decodeRecord(scope, tombstone).generation == 2);
        javaRecord[12] ^= 1;
        try { StartupSelectionStore.decodeRecord(scope, javaRecord); throw new AssertionError(); }
        catch (StartupSelectionStore.StoreException expected) { require(expected.status.equals("Corrupt")); }
        try { StartupSelectionStore.encodeRecord(scope, 3, 3); throw new AssertionError(); }
        catch (StartupSelectionStore.StoreException expected) { require(expected.status.equals("InvalidMode")); }
        System.out.println("Java protocol/native record byte equivalence, tombstone and corrupt/invalid input: passed. Android storage/device gates NOT run.");
    }
}
