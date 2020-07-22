Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Web.Script.Serialization

Module Extension
    Dim endpoint As String = ""
    Dim certificate As String
    Dim subject As String = "ICP-Brasil"
    Dim token As String
    Dim authkey As String
    Dim cpf As String

    Sub cert()
        Dim subjectRegEx As String = "ICP-Brasil"
        '    subjectRegEx = certificaterequest.subject

        certificate = getCertificate("Assinatura Digital", "Escolha o certificado que será utilizado na assinatura.", subjectRegEx, "")
        subject = getSubject()

        If String.IsNullOrEmpty(certificate) Then
            MsgBox("Nenhum certificado encontrado.", vbOKOnly, "Erro!")
        End If
    End Sub

    Function currentcert() As String
        certificate = getCertificate("Assinatura Digital", "Escolha o certificado que será utilizado na assinatura.", subject, "")
        subject = getSubject()

        If String.IsNullOrEmpty(certificate) Then
            Return Nothing
        End If

        Return certificate
    End Function

    Public Class RestException
        Inherits Exception
        Private context As String
        Private response As RestResponse
        Private errorresp As RestResponse = New RestResponse

        Public Sub New(ctx As String, ex As Exception)
            MyBase.New(ex.Message, ex)
            context = ctx
            response = Nothing
            errorresp.errormsg = ex.Message
            ReDim errorresp.errordetails(0)
            errorresp.errordetails(0) = New ErrorDetail
            With errorresp.errordetails(0)
                .logged = False
                .presentable = False
                .service = "bluecrystal.exe"
                .stacktrace = ex.GetType().FullName & ": " & ex.Message & vbCrLf & ex.StackTrace
                .context = context
            End With
        End Sub

        Public Sub New(ctx As String, ex As Exception, r As RestResponse)
            MyBase.New(r.errormsg, ex)
            context = ctx
            response = r
            errorresp.errormsg = response.errormsg

            Dim ed As ErrorDetail = New ErrorDetail
            ed.stacktrace = ex.GetType().FullName & ": " & ex.Message & vbCrLf & ex.StackTrace
            ed.service = "bluecrystal.exe"
            ed.context = context

            If Not IsNothing(response.errordetails) AndAlso response.errordetails.Length > 0 Then
                ReDim errorresp.errordetails(response.errordetails.Length)
                With response.errordetails(response.errordetails.Length - 1)
                    ed.logged = .logged
                    ed.presentable = .presentable
                End With

                Dim i As Integer = 0
                For Each edr In response.errordetails
                    errorresp.errordetails(i) = edr
                    i += 1
                Next
                errorresp.errordetails(i) = ed
            Else
                ReDim errorresp.errordetails(0)
                errorresp.errordetails(0) = ed
            End If
        End Sub

        Public Function ToJSON() As String
            Dim jsonSerializer As New JavaScriptSerializer
            Return jsonSerializer.Serialize(errorresp)
        End Function
    End Class

    Private Function http(Of T As RestResponse)(context As String, method As String, payload As Object) As T
        Dim ex As Exception = Nothing
        Dim obj As T = Nothing
        Try
            Dim jsonSerializer As New JavaScriptSerializer
            Dim jsonpayloadrequest As String = jsonSerializer.Serialize(payload)
            Dim byteArray As Byte() = Encoding.UTF8.GetBytes(jsonpayloadrequest)

            Dim request As WebRequest = WebRequest.Create(EndPoint & method)
            request.Method = "POST"
            request.ContentType = "application/json"
            request.ContentLength = byteArray.Length
            Dim dataStream As Stream = request.GetRequestStream()
            request.GetRequestStream()
            dataStream.Write(byteArray, 0, byteArray.Length)
            Dim response As WebResponse
            Try
                response = request.GetResponse()
            Catch e As WebException
                response = e.Response()
                ex = e
            End Try
            Dim reader As New StreamReader(response.GetResponseStream())
            Dim jsonOperationGetResponse As String = reader.ReadToEnd()
            obj = jsonSerializer.Deserialize(Of T)(jsonOperationGetResponse)
        Catch exep As Exception
            Throw New RestException(context, exep)
        End Try

        If Not IsNothing(ex) OrElse (Not IsNothing(obj) AndAlso Not IsNothing(obj.errormsg)) Then
            Throw New RestException(context, ex, obj)
        End If

        Return obj
    End Function

    Function getTokenString() As String
        Dim tokenrequest As New TokenRequest
        tokenrequest.certificate = certificate

        Dim tokenresponse As TokenResponse = http(Of TokenResponse)("obter o token", "/token", tokenrequest)
        Return tokenresponse.token
    End Function

    Sub produceToken()
        Dim sToken As String = getTokenString()

        If (Not sToken.StartsWith("TOKEN-")) Then
            Throw New System.Exception("Token should start with TOKEN-.")
        End If
        If (sToken.Length > 128 Or sToken.Length < 16) Then
            Throw New System.Exception("Token too long or too shor.")
        End If

        Dim datetime() As Byte = Encoding.UTF8.GetBytes(sToken)
        ' Dim subject() As Byte = Encoding.UTF8.GetBytes(tokenrequest.subject)
        'Dim payload(datetime.Length + subject.Length - 1) As Byte
        'Buffer.BlockCopy(datetime, 0, payload, 0, datetime.Length)
        'Buffer.BlockCopy(subject, 0, payload, datetime.Length, subject.Length)
        Dim payloadAsString As String = Convert.ToBase64String(datetime)

        Dim tokenresponse As New TokenResponse
        token = sToken & ";" & BluC.sign(99, payloadAsString)
    End Sub

    Function getToken() As String
        If token Is Nothing Then
            produceToken()
        End If
        Return token
    End Function

    Sub produceAuthKey()
        getToken()
        Dim authrequest As New AuthRequest
        authrequest.token = token

        Dim authresponse As AuthResponse = http(Of AuthResponse)("autenticar", "/auth", authrequest)
        authkey = authresponse.authkey
        cpf = authresponse.cpf
    End Sub

    Function getAuthKey() As String
        If authkey Is Nothing Then
            produceAuthKey()
        End If
        Return authkey
    End Function

    Function getCPF() As String
        If cpf Is Nothing Then
            produceAuthKey()
        End If
        Return cpf
    End Function

    Function getList() As Doc()
        Dim jsonSerializer As New JavaScriptSerializer

        Dim listrequest As New ListRequest

        listrequest.certificate = certificate
        listrequest.authkey = authkey

        Dim listresponse As ListResponse = http(Of ListResponse)("listar documentos", "/list", listrequest)
        Return listresponse.list
    End Function

    Private Function getHash(d As Doc) As HashResponse
        Dim jsonSerializer As New JavaScriptSerializer

        Dim hashrequest As New HashRequest

        hashrequest.certificate = certificate
        hashrequest.authkey = authkey
        hashrequest.system = d.system
        hashrequest.id = d.id

        Dim hashresponse As HashResponse = http(Of HashResponse)("obter o hash", "/hash", hashrequest)
        Return hashresponse
    End Function

    Private Function saveSign(d As Doc, hashresponse As HashResponse, signature As String, ByRef saveResp As String) As String
        Dim jsonSerializer As New JavaScriptSerializer

        Dim saverequest As New SaveRequest

        saverequest.signature = signature

        saverequest.certificate = hashresponse.certificate
        saverequest.time = hashresponse.time
        saverequest.sha1 = hashresponse.sha1
        saverequest.sha256 = hashresponse.sha256
        saverequest.policy = hashresponse.policy
        saverequest.id = d.id
        saverequest.system = d.system

        Dim saveresponse As SaveResponse = http(Of SaveResponse)("gravar assinatura", "/save", saverequest)
        If Not IsNothing(saveresponse.errormsg) Then
            Return "Erro: " & saveresponse.errormsg
        End If
        Return saveresponse.status
    End Function

    Function sign(d As Doc, ByRef saveResp As String) As String
        Try
            Dim hashresponse As HashResponse = getHash(d)
            Dim s As String = produceSign(hashresponse.policy, hashresponse.hash)
            Return saveSign(d, hashresponse, s, saveResp)
        Catch rex As RestException
            Throw rex
        Catch ex As Exception
            Throw New RestException("assinar", ex)
        End Try
    End Function

    Function produceSign(policy As String, payload As String) As String
        Dim keySize = getKeySize()
        Dim s As String
        If policy = "PKCS7" Then
            s = BluC.sign(99, payload)
        ElseIf keySize < 2048 Then
            s = BluC.sign("sha1", payload)
        Else
            s = BluC.sign("sha256", payload)
        End If
        Return s
    End Function

    Private Function store(payload As String) As StoreResponse
        Dim jsonSerializer As New JavaScriptSerializer

        Dim storerequest As New StoreRequest

        storerequest.payload = payload
        Dim jsonpayloadrequest As String = jsonSerializer.Serialize(storerequest)
        Dim byteArray As Byte() = Encoding.UTF8.GetBytes(jsonpayloadrequest)

        Return http(Of StoreResponse)("armazenar", "/store", storerequest)
    End Function

    Private Function jsonStringSafe(s As String) As String
        s = s.Replace(vbCr, " ")
        s = s.Replace(vbLf, " ")
        Return s
    End Function

    Public Class ErrorDetail
        Public logged As Boolean
        Public presentable As Boolean
        Public service As String
        Public stacktrace As String
        Public context As String
    End Class

    Public Class RestResponse
        Public errormsg As String
        Public errordetails() As ErrorDetail
    End Class

    Private Class SignRequest
        Public payload As String
        Public certificate As String
        Public subject As String
        Public policy As String
    End Class

    Private Class SignResponse
        Inherits RestResponse

        Public sign As String
        Public signkey As String
        Public subject As String
    End Class

    Private Class TokenRequest
        Public certificate As String
    End Class

    Private Class TokenResponse
        Inherits RestResponse

        Public policy As String
        Public token As String
    End Class

    Private Class AuthRequest
        Public token As String
    End Class

    Private Class AuthResponse
        Inherits RestResponse

        Public authkey As String
        Public cpf As String
    End Class


    Private Class ListRequest
        Public certificate As String
        Public authkey As String
    End Class

    Private Class ListResponse
        Inherits RestResponse

        Public list() As Doc
    End Class

    Public Class Doc
        Public code As String
        Public descr As String
        Public system As String
        Public id As String
        Public kind As String
        Public origin As String
    End Class

    Private Class HashRequest
        Public system As String
        Public id As String
        Public certificate As String
        Public authkey As String
    End Class

    Private Class HashResponse
        Inherits RestResponse

        Public hash As String
        Public policyversion As String
        Public policy As String
        Public certificate As String
        Public time As String
        Public sha1 As String
        Public sha256 As String
        Public extra As String
    End Class

    Private Class SaveRequest
        Public system As String
        Public id As String
        Public code As String
        Public signature As String
        Public policy As String
        Public certificate As String
        Public time As String
        Public sha1 As String
        Public sha256 As String
        Public extra As String
    End Class

    Private Class SaveResponse
        Inherits RestResponse

        Public status As String
    End Class

    Private Class StoreRequest
        Public payload As String
    End Class

    Private Class StoreResponse
        Inherits RestResponse

        Public key As String
        Public status As String
    End Class
End Module
