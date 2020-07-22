﻿Imports System
Imports System.Security.Cryptography
Imports System.Security.Permissions
Imports System.IO
Imports System.Security.Cryptography.X509Certificates

Imports System.Text.RegularExpressions
Imports System.Security.Cryptography.Pkcs

Module BluC
    Private certificate As X509Certificate2

    Public Function getCertificate(title As String, message As String, subjectRegex As String, issuerRegex As String) As String
        Dim ret As String = ""
        Dim store As X509Store = New X509Store(StoreName.My, StoreLocation.CurrentUser)
        store.Open(OpenFlags.OpenExistingOnly)
        Dim certificates As X509Certificate2Collection = store.Certificates
        Dim certificatesFiltered As X509Certificate2Collection = New X509Certificate2Collection()
        Dim enumCert As X509Certificate2Enumerator = certificates.GetEnumerator()
        While (enumCert.MoveNext())
            Dim certificateTmp As X509Certificate2 = enumCert.Current
            Dim subjectOk As Boolean = True
            If subjectRegex.Length > 0 Then
                subjectOk = certificateTmp.Subject = subjectRegex
                If (Not subjectOk) Then
                    Dim matchSubject As Match = Regex.Match(certificateTmp.Subject, subjectRegex, RegexOptions.IgnoreCase)
                    subjectOk = matchSubject.Success
                End If
            End If

            Dim issuerOk As Boolean = True
            If issuerRegex.Length > 0 Then
                Dim matchIssuer As Match = Regex.Match(certificateTmp.Issuer, issuerRegex, RegexOptions.IgnoreCase)
                issuerOk = matchIssuer.Success
            End If

            Dim dateOk As Boolean = Now > certificateTmp.NotBefore AndAlso Now < certificateTmp.NotAfter

            If subjectOk And issuerOk And dateOk Then
                certificatesFiltered.Add(certificateTmp)
            End If
        End While

        If certificatesFiltered.Count = 0 Then
            certificate = Nothing
            Return ""
        ElseIf certificatesFiltered.Count = 1 Then
            certificate = certificatesFiltered(0)
        Else
            Dim certificateSel As X509Certificate2Collection = X509Certificate2UI.SelectFromCollection(certificatesFiltered, title, message, X509SelectionFlag.SingleSelection)
            If certificateSel.Count > 0 Then
                certificate = certificateSel(0)
            End If
        End If
        Dim certAsByte As Byte() = certificate.Export(X509ContentType.Cert)
        Dim certAsString As String = Convert.ToBase64String(certAsByte)
        ret = certAsString
        Return ret
    End Function

    Public Sub clearCurrentCertificate()
        certificate = Nothing
    End Sub

    Public Function getCertificateBySubject(subject As String) As String
        Dim ret As String = ""
        Dim store As X509Store = New X509Store(StoreName.My, StoreLocation.CurrentUser)
        store.Open(OpenFlags.OpenExistingOnly)
        Dim certificates As X509Certificate2Collection = store.Certificates
        Dim certificatesFiltered As X509Certificate2Collection = New X509Certificate2Collection()
        Dim enumCert As X509Certificate2Enumerator = certificates.GetEnumerator()
        While (enumCert.MoveNext())
            Dim certificateTmp As X509Certificate2 = enumCert.Current
            If certificateTmp.Subject = subject Then
                certificate = certificateTmp
                Dim certAsByte As Byte() = certificate.Export(X509ContentType.Cert)
                Dim certAsString As String = Convert.ToBase64String(certAsByte)
                Return certAsString
            End If
        End While
        Return Nothing
    End Function

    Public Function getSubject() As String
        If certificate Is Nothing Then
            Return ""
        End If
        Return certificate.Subject
    End Function

    Public Function getKeySize() As Integer
        Dim publicKey As RSACryptoServiceProvider = DirectCast(certificate.PublicKey.Key, RSACryptoServiceProvider)
        Return publicKey.KeySize
    End Function

    Public Function sign(hashAlg As String, contentB64 As String) As String
        Return sign(convertHashAlg(hashAlg), contentB64)
    End Function

    Public Function sign(hashAlg As Integer, contentB64 As String) As String
        Dim content As Byte() = Convert.FromBase64String(contentB64)
        Return Convert.ToBase64String(sign(hashAlg, content))
    End Function

    Public Function SignMsg(hashAlg As Integer, msg As Byte(), signerCert As X509Certificate2) As Byte()
        Dim contentInfo As ContentInfo = New ContentInfo(msg)
        Dim signedCms As SignedCms = New SignedCms(contentInfo, True)
        Dim cmsSigner As CmsSigner = New CmsSigner(signerCert)
        signedCms.ComputeSignature(cmsSigner, False)
        Dim ab As Byte() = signedCms.Encode()

        ' Dim signedCms2 As SignedCms = New SignedCms(contentInfo)
        ' signedCms2.Decode(ab)
        ' Console.WriteLine(signedCms2.Detached)
        ' Dim ab2 As Byte() = signedCms2.Encode()
        ' Dim dettachedB64 As String = Convert.ToBase64String(ab)
        ' Dim attachedB64 As String = Convert.ToBase64String(ab2)
        ' Dim msgB64 As String = Convert.ToBase64String(msg)

        Return ab
    End Function

    Public Function sign(hashAlg As Integer, content As Byte()) As Byte()
        Dim hash As HashAlgorithm = Nothing
        Dim signature As Byte()

        If hashAlg = 99 Then
            signature = SignMsg(hashAlg, content, certificate)
        Else
            Select Case hashAlg
                Case 0
                    hash = New SHA1Managed()
                Case 1
                    Throw New Exception("unsupported algorithm")
                Case 2
                    hash = New SHA256Managed()
                Case 3
                    hash = New SHA384Managed()
                Case 4
                    hash = New SHA512Managed()
            End Select

            ' Note that this will return a Basic crypto provider, with only SHA-1 support
            Dim privateKey As RSACryptoServiceProvider = DirectCast(certificate.PrivateKey, RSACryptoServiceProvider)
            Dim publicKey As RSACryptoServiceProvider = DirectCast(certificate.PublicKey.Key, RSACryptoServiceProvider)

            Try
                signature = privateKey.SignData(content, hash)
            Catch ex As Exception
                ' Force use of the Enhanced RSA And AES Cryptographic Provider with openssl-generated SHA256 keys
                Dim enhCsp = New RSACryptoServiceProvider().CspKeyContainerInfo
                Dim cspparams = New CspParameters(enhCsp.ProviderType, enhCsp.ProviderName, privateKey.CspKeyContainerInfo.KeyContainerName)
                Dim enhancedPrivateKey As RSACryptoServiceProvider = New RSACryptoServiceProvider(cspparams)
                signature = enhancedPrivateKey.SignData(content, hash)
            End Try

            Dim verify As Boolean = False
            verify = publicKey.VerifyData(content, hash, signature)
        End If
        Return signature
    End Function

    Public Function convertHashAlg(hashAlg As String) As Integer
        Dim tmp As String = hashAlg.ToUpper()
        Select Case tmp
            Case "SHA1", "0"
                Return 0
            Case "SHA256", "2"
                Return 2
            Case "PKCS7", "99"
                Return 99
        End Select
        Throw New Exception("Hash alg not recognized: " + hashAlg)
    End Function
End Module
