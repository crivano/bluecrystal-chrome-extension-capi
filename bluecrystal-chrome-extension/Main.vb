Imports System.Deployment
Imports System.IO
Imports System.Text
Imports System.Web.Script.Serialization

Module Main

    Sub Main()
        'MsgBox("Assinador Carregado 12", vbOKOnly)
        Dim encoder As New UTF8Encoding()
        Dim inputStream As Stream = Console.OpenStandardInput()
        Dim outputStream As Stream = Console.OpenStandardOutput()

        Do
            Dim bytes(3) As Byte
            Dim bytesLength As Integer = inputStream.Read(bytes, 0, 4)
            Dim contentLength As Long = bytes(0) + bytes(1) * (256 ^ 1) + bytes(2) * (256 ^ 2) + bytes(3) * (256 ^ 3)
            'MsgBox("Recebido length " & contentLength, vbOKOnly)
            'Console.Error.WriteLine("request bytes: " & contentLength)

            If contentLength = 0 Then
                'MsgBox("abandonando...")
                Exit Do
            End If

            Dim content(contentLength - 1) As Byte
            Dim length As Integer = content.Length
            Dim read As Integer = 0
            Dim offset As Integer = 0

            Do
                read = inputStream.Read(content, offset, length - offset)
                offset += read
            Loop Until offset = length

            Dim s As String = encoder.GetString(content, 0, content.Length)
            'MsgBox("Recebido string " & s, vbOKOnly)
            'Console.Error.WriteLine("request string: " & s)

            Dim response = Run(s)

            'MsgBox("Enviando string " & response, vbOKOnly)
            'Console.Error.WriteLine("response string: " & response)
            Dim output() As Byte = encoder.GetBytes(response)

            Dim l As Long = output.Length
            For i = 0 To 3
                Dim j As Integer = l Mod 256
                l = l \ 256
                outputStream.WriteByte(j)
            Next
            outputStream.Write(output, 0, output.Length)
        Loop
    End Sub

    Private Function Run(ByVal msg As String) As String
        Dim jsonOut As String = ""
        Try
            Dim jsonSerializer As New JavaScriptSerializer

            Dim genericrequest As GenericRequest = jsonSerializer.Deserialize(Of GenericRequest)(msg)

            If genericrequest.url.EndsWith("/test") Then
                jsonOut = test()
            ElseIf genericrequest.url.EndsWith("/currentcert") Then
                jsonOut = currentcert()
            ElseIf genericrequest.url.EndsWith("/cert") Then
                jsonOut = cert(genericrequest.data)
            ElseIf genericrequest.url.EndsWith("/token") Then
                jsonOut = token(genericrequest.data)
            ElseIf genericrequest.url.EndsWith("/sign") Then
                jsonOut = sign(genericrequest.data)
            Else
                Return "{""success"":false,""data"":{""errormsg"":""Error 404: file not found""}}"
            End If

            Return "{""success"":true,""data"":" & jsonOut & "}"
        Catch ex As Exception
            Dim message As String = ex.Message
            If message.StartsWith("O conjunto de chaves não") Then
                message = "Não localizamos nenhum Token válido no computador. Por favor, verifique se foi corretamente inserido."
            End If
            Return "{""success"":false,""status"":500,""data"":{""errormsg"":""" + jsonStringSafe(message) + """}}"
        End Try
    End Function


    Function test() As String
        Dim jsonSerializer As New JavaScriptSerializer

        Dim testresponse As New TestResponse
        testresponse.provider = "BlueCrystal Signer Extension"
        testresponse.version = "1.4.0.0"
        testresponse.status = "OK"
        Dim jsonOut As String = jsonSerializer.Serialize(testresponse)

        Return jsonOut
    End Function

    Function currentcert() As String
        Dim jsonSerializer As New JavaScriptSerializer

        Dim certificateresponse As New CertificateResponse
        certificateresponse.subject = getSubject()
        If Not String.IsNullOrEmpty(certificateresponse.subject) Then
            certificateresponse.certificate = getCertificate("Assinatura Digital", "Escolha o certificado que será utilizado na assinatura.", certificateresponse.subject, "")
            certificateresponse.subject = getSubject()
        End If

        If String.IsNullOrEmpty(certificateresponse.subject) Then
            certificateresponse.subject = Nothing
            certificateresponse.errormsg = "Nenhum certificado ativo no momento."
        End If

        Dim jsonOut As String = jsonSerializer.Serialize(certificateresponse)

        Return jsonOut
    End Function

    Function cert(req As RequestData) As String
        Dim jsonSerializer As New JavaScriptSerializer

        Dim subjectRegEx As String = "ICP-Brasil"

        If (Not req Is Nothing) AndAlso (Not String.IsNullOrEmpty(req.subject)) Then
            subjectRegEx = req.subject
        End If

        Dim certificateresponse As New CertificateResponse
        certificateresponse.certificate = getCertificate("Assinatura Digital", "Escolha o certificado que será utilizado na assinatura.", subjectRegEx, "")
        certificateresponse.subject = getSubject()

        If String.IsNullOrEmpty(certificateresponse.certificate) Then
            certificateresponse.errormsg = "Nenhum certificado encontrado."
        End If

        Dim jsonOut As String = jsonSerializer.Serialize(certificateresponse)

        Return jsonOut
    End Function

    Function token(req As RequestData) As String
        Dim jsonSerializer As New JavaScriptSerializer

        Try
            If req.subject <> Nothing Then
                Dim s As String = BluC.getCertificateBySubject(req.subject)
            End If

            If (Not req.token.StartsWith("TOKEN-")) Then
                Throw New System.Exception("Token should start with TOKEN-.")
            End If
            If (req.token.Length > 128 Or req.token.Length < 16) Then
                Throw New System.Exception("Token too long or too shor.")
            End If

            Dim datetime() As Byte = Encoding.UTF8.GetBytes(req.token)
            ' Dim subject() As Byte = Encoding.UTF8.GetBytes(tokenrequest.subject)
            'Dim payload(datetime.Length + subject.Length - 1) As Byte
            'Buffer.BlockCopy(datetime, 0, payload, 0, datetime.Length)
            'Buffer.BlockCopy(subject, 0, payload, datetime.Length, subject.Length)
            Dim payloadAsString As String = Convert.ToBase64String(datetime)

            Dim tokenresponse As New TokenResponse
            tokenresponse.sign = BluC.sign(99, payloadAsString)

            tokenresponse.subject = getSubject()
            tokenresponse.token = req.token

            Dim jsonOut As String = jsonSerializer.Serialize(tokenresponse)
            Return jsonOut
        Catch ex As Exception
            BluC.clearCurrentCertificate()
            Throw ex
        End Try
    End Function

    Function sign(req As RequestData) As String
        'Throw New SystemException("error...")
        Dim jsonSerializer As New JavaScriptSerializer

        If req.subject <> Nothing Then
            Dim s As String = BluC.getCertificateBySubject(req.subject)
        End If

        Dim keySize = getKeySize()
        Dim signresponse As New SignResponse
        If req.policy = "PKCS7" Then
            signresponse.sign = BluC.sign(99, req.payload)
        ElseIf keySize < 2048 Then
            signresponse.sign = BluC.sign("sha1", req.payload)
        Else
            signresponse.sign = BluC.sign("sha256", req.payload)
        End If

        signresponse.subject = getSubject()

#If False Then
        Dim storeresponse As StoreResponse = store(signresponse.sign)
        If storeresponse.status <> "OK" Then
            Throw New SystemException(storeresponse.errormsg)
        End If
        signresponse.signkey = storeresponse.key
        signresponse.sign = ""
#End If

        Dim jsonOut As String = jsonSerializer.Serialize(signresponse)
        Return jsonOut
    End Function

    Private Function jsonStringSafe(s As String) As String
        s = s.Replace(vbCr, " ")
        s = s.Replace(vbLf, " ")
        Return s
    End Function

    Public Class RequestData
        Public certificate As String
        Public subject As String
        Public payload As String
        Public policy As String
        Public code As String
        Public token As String
    End Class

    Private Class GenericRequest
        Public url As String
        Public data As RequestData
    End Class

    Private Class GenericResponse
        Public errormsg As String
    End Class

    Private Class TestResponse
        Public provider As String
        Public version As String
        Public status As String
        Public errormsg As String
    End Class


    Private Class CertificateResponse
        Public certificate As String
        Public subject As String
        Public errormsg As String
    End Class

    Private Class SignResponse
        Public sign As String
        Public signkey As String
        Public subject As String
        Public errormsg As String
    End Class

    Private Class TokenResponse
        Public sign As String
        Public token As String
        Public subject As String
        Public errormsg As String
    End Class

End Module
