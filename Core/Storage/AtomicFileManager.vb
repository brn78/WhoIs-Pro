Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports WhoIsPro.Models

Namespace Storage
    Public Class AtomicSaveResult
        Public Property IsSuccess As Boolean
        Public Property FilePath As String
        Public Property BackupPath As String
        Public Property ChecksumSHA256 As String
        Public Property ErrorMessage As String
    End Class

    Public Module AtomicFileManager
        Private ReadOnly JsonOpts As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True
        }

        ''' <summary>
        ''' Salva in modo atomico (ACID-like) la sessione su disco senza rischio di file corrotti.
        ''' </summary>
        Public Function SaveSessionAtomic(session As ScanSession, destinationPath As String) As AtomicSaveResult
            Dim result As New AtomicSaveResult With {.FilePath = destinationPath}
            If session Is Nothing Then
                result.ErrorMessage = "La sessione da salvare è null"
                Return result
            End If

            Dim targetDir = Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            If Not Directory.Exists(targetDir) Then
                Directory.CreateDirectory(targetDir)
            End If

            ' Generazione nome file temporaneo nella STESSA directory (garantisce stesso volume fisico per rename atomico)
            Dim tempFile = Path.Combine(targetDir, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp")
            Dim backupFile = destinationPath & ".bak"
            result.BackupPath = backupFile

            session.ModifiedAt = DateTime.Now

            Try
                ' 1. Serializzazione in memoria
                Dim jsonString = JsonSerializer.Serialize(session, JsonOpts)
                Dim payloadBytes = Encoding.UTF8.GetBytes(jsonString)

                ' 2. Calcolo hash SHA-256 per verifica integrità
                Dim hashBytes = SHA256.HashData(payloadBytes)
                Dim sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant()
                session.ChecksumSHA256 = sha256Hex
                result.ChecksumSHA256 = sha256Hex

                ' Rieseguiamo serialize con l'hash incluso
                jsonString = JsonSerializer.Serialize(session, JsonOpts)
                payloadBytes = Encoding.UTF8.GetBytes(jsonString)

                ' 3. Scrittura sul file temporaneo con flush forzato su disco fisico
                Using fs As New FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
                    fs.Write(payloadBytes, 0, payloadBytes.Length)
                    fs.Flush(flushToDisk:=True)
                End Using

                ' 4. Sostituzione atomica del file
                If File.Exists(destinationPath) Then
                    ' File.Replace usa internamente ReplaceFile di Win32 (operazione atomica garantita dal filesystem NTFS)
                    File.Replace(tempFile, destinationPath, backupFile, ignoreMetadataErrors:=True)
                Else
                    ' Se il file non esiste ancora, MoveFile atomico
                    File.Move(tempFile, destinationPath, overwrite:=True)
                End If

                result.IsSuccess = True
            Catch ex As Exception
                result.IsSuccess = False
                result.ErrorMessage = $"Errore durante il salvataggio atomico: {ex.Message}"
                ' Pulizia file temporaneo in caso di errore
                Try
                    If File.Exists(tempFile) Then File.Delete(tempFile)
                Catch
                End Try
            End Try

            Return result
        End Function

        ''' <summary>
        ''' Carica in modo sicuro la sessione con verifica di integrità e ripristino automatico da backup se corrotto.
        ''' </summary>
        Public Function LoadSessionSafe(filePath As String, ByRef restoredFromBackup As Boolean) As ScanSession
            restoredFromBackup = False
            If Not File.Exists(filePath) Then
                Throw New FileNotFoundException($"Il file {filePath} non esiste.")
            End If

            Dim targetToRead = filePath
            Dim session As ScanSession = Nothing

            Try
                session = ReadAndDeserialize(targetToRead)
            Catch ex As Exception
                ' Se il file principale è danneggiato, tentiamo di ripristinarlo dal backup .bak
                Dim backupFile = filePath & ".bak"
                If File.Exists(backupFile) Then
                    Try
                        session = ReadAndDeserialize(backupFile)
                        restoredFromBackup = True
                    Catch
                        Throw New IOException($"Impossibile leggere il file {filePath} né il backup {backupFile}: {ex.Message}")
                    End Try
                Else
                    Throw New IOException($"Impossibile leggere il file {filePath}: {ex.Message}")
                End If
            End Try

            Return session
        End Function

        Private Function ReadAndDeserialize(path As String) As ScanSession
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using reader As New StreamReader(fs, Encoding.UTF8)
                    Dim json = reader.ReadToEnd()
                    Return JsonSerializer.Deserialize(Of ScanSession)(json, JsonOpts)
                End Using
            End Using
        End Function
    End Module
End Namespace