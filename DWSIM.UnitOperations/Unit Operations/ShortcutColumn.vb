'    Shortcut Column Calculation Routines 
'    Copyright 2008-2013 Daniel Wagner O. de Medeiros
'
'    This file is part of DWSIM.
'
'    DWSIM is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    DWSIM is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with DWSIM.  If not, see <http://www.gnu.org/licenses/>.
'

Imports System.Collections.Generic
Imports DWSIM.DrawingTools.GraphicObjects
Imports DWSIM.DWSIM.SimulationObjects
Imports DWSIM.Thermodynamics.MathEx
Imports System.Math
Imports DWSIM.DWSIM.Flowsheet.FlowsheetSolver

Namespace UnitOperations

    <System.Serializable()> Public Class ShortcutColumn

        Inherits SharedClasses.UnitOperations.BaseClass

        Public Enum CondenserType
            TotalCond = 0
            PartialCond = 1
        End Enum

        Public m_lightkey As String = ""
        Public m_heavykey As String = ""
        Public m_lightkeymolarfrac As Double = 0.001
        Public m_heavykeymolarfrac As Double = 0.001
        Public m_refluxratio As Double = 1.5
        Public m_boilerpressure As Double = 0
        Public m_condenserpressure As Double = 0

        Public m_N, m_Nmin, m_Rmin, m_Tc, m_Tb, m_Qc, m_Qb, L, V, L_, V_, ofs As Double

        Public condtype As CondenserType = CondenserType.TotalCond

        Public Sub New()
            MyBase.New()
        End Sub

        Public Sub New(ByVal name As String, ByVal description As String)

            MyBase.CreateNew()
            ComponentName = name
            ComponentDescription = description

        End Sub

        Public Overrides Function LoadData(data As System.Collections.Generic.List(Of System.Xml.Linq.XElement)) As Boolean
            MyBase.LoadData(data)
            XMLSerializer.XMLSerializer.Deserialize(Me, data, True)
            Return True
        End Function

        Public Overrides Function SaveData() As System.Collections.Generic.List(Of System.Xml.Linq.XElement)
            Dim elements As System.Collections.Generic.List(Of System.Xml.Linq.XElement) = MyBase.SaveData()
            elements.AddRange(XMLSerializer.XMLSerializer.Serialize(Me, True))
            Return elements
        End Function

        Public Overrides Sub Validate()

            Dim objargs As New DWSIM.Extras.StatusChangeEventArgs

            If Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.Vessel
                End With

                Throw New Exception(Me.FlowSheet.GetTranslatedString("Nohcorrentedematriac10"))
            ElseIf Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.Vessel
                End With

                Throw New Exception(Me.FlowSheet.GetTranslatedString("Nohcorrentedematriac11"))
            ElseIf Not Me.GraphicObject.OutputConnectors(1).IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.Vessel
                End With

                Throw New Exception(Me.FlowSheet.GetTranslatedString("Nohcorrentedematriac11"))
            ElseIf Not Me.GraphicObject.EnergyConnector.IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.Vessel
                End With

                Throw New Exception(Me.FlowSheet.GetTranslatedString("Verifiqueasconexesdo"))
            ElseIf Not Me.GraphicObject.InputConnectors(1).IsAttached Then
                'Call function to calculate flowsheet
                With objargs
                    .Calculated = False
                    .Name = Me.Name
                    .ObjectType = ObjectType.Vessel
                End With

                Throw New Exception(Me.FlowSheet.GetTranslatedString("Verifiqueasconexesdo"))
            End If

        End Sub

        Public Overrides Function Calculate(Optional ByVal args As Object = Nothing) As Integer

            Dim objargs As New DWSIM.Extras.StatusChangeEventArgs

            'Validate unitop status.
            Me.Validate()

            'streams

            Dim feed, distillate, bottoms As Streams.MaterialStream
            Dim cduty, rduty As Streams.EnergyStream

            feed = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)
            distillate = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Name)
            bottoms = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(1).AttachedConnector.AttachedTo.Name)
            cduty = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.EnergyConnector.AttachedConnector.AttachedTo.Name)
            rduty = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.InputConnectors(1).AttachedConnector.AttachedFrom.Name)

            feed.Validate()

            'classify components by relative volatility

            Dim n As Integer = feed.Phases(0).Compounds.Count - 1
            Dim i As Integer = 0

            Dim lnk, dnk, hnk As New ArrayList
            Dim hki, lki As Integer
            Dim K(n), alpha(n), z(n), xb(n), xd(n), F, D, Dant, B, R, q, T, P As Double
            Dim id(n) As String

            F = feed.Phases(0).Properties.molarflow.GetValueOrDefault
            q = feed.Phases(1).Properties.molarfraction.GetValueOrDefault
            T = feed.Phases(0).Properties.temperature.GetValueOrDefault
            P = feed.Phases(0).Properties.pressure.GetValueOrDefault

            i = 0
            For Each comp As Interfaces.ICompound In feed.Phases(0).Compounds.Values
                z(i) = comp.MoleFraction.GetValueOrDefault
                'K(i) = feed.Phases(2).Compounds(comp.Name).MoleFraction.GetValueOrDefault / feed.Phases(1).Compounds(comp.Name).MoleFraction.GetValueOrDefault
                id(i) = comp.Name
                'If Double.IsInfinity(K(i)) Then K(i) = 1.0E+20
                If Me.m_lightkey = comp.Name Then lki = i
                If Me.m_heavykey = comp.Name Then hki = i
                i = i + 1
            Next

            feed.PropertyPackage.CurrentMaterialStream = feed

            K = feed.PropertyPackage.DW_CalcKvalue(z, T, P)

            For i = 0 To n
                If Double.IsInfinity(K(i)) Then K(i) = 1.0E+20
            Next

            i = 0
            Do
                alpha(i) = K(i) / K(hki)
                If K(i) > K(lki) Then
                    lnk.Add(i)
                ElseIf K(i) < K(lki) And K(i) > K(hki) Then
                    dnk.Add(i)
                ElseIf K(i) < K(hki) Then
                    hnk.Add(i)
                End If
                i = i + 1
            Loop Until i = n + 1

            'first D estimate
            i = 0
            D = F * z(lki)
            If lnk.Count > 0 Then
                Do
                    D += F * (z(lnk(i)))
                    i = i + 1
                Loop Until i >= lnk.Count
            End If

restart:    B = F - D

            xd(hki) = Me.m_heavykeymolarfrac
            xb(lki) = Me.m_lightkeymolarfrac

            xb(hki) = (F * z(hki) - D * xd(hki)) / (F - D)
            xd(lki) = (F * z(lki) - (F - D) * xb(lki)) / D

            'Nmin calculation (Fenske)

            Dim S As Double

            S = (xd(lki) / xd(hki)) * (xb(hki) / xb(lki))
            m_Nmin = Math.Log(S) / Math.Log(alpha(lki) / alpha(hki))

            'calculate nonkeys distribution

            Dim C, cte(n) As Double

            C = (Log10(alpha(lki)) * Log10(xd(hki) / xb(hki)) - Log10(alpha(hki)) * Log10(xd(lki) / xb(lki))) / (Log10(alpha(lki)) - Log10(alpha(hki)))

            i = 0
            Do
                If i <> lki And i <> hki Then
                    cte(i) = 10 ^ (m_Nmin * Math.Log10(alpha(i)) + C)
                    xb(i) = F * z(i) / (B + D * cte(i))
                    xd(i) = xb(i) * cte(i)
                End If
                i = i + 1
            Loop Until i = n + 1

            Dant = D

            i = 0
            D = 0
            Do
                If z(i) <> 0 Then D += Dant * xd(i)
                i = i + 1
            Loop Until i = n + 1

            If Double.IsNaN(D) Or D = 0.0# Then Throw New ArgumentOutOfRangeException("D", "Invalid value for Distillate Rate: " & D)

            CheckCalculatorStatus()

            If Not Math.Abs((D - Dant) / D) < 0.0001 Then GoTo restart

            R = m_refluxratio
            L = R * D
            L_ = L + q * F
            V_ = L_ - B
            V = D + L

            'calculate minimum reflux by Underwood's method

            Dim brentsolver As New BrentOpt.Brent
            brentsolver.DefineFuncDelegate(AddressOf rminfunc)

            Dim mode2 As Boolean = False
            Dim count As Integer = 0
            Dim indexes As New ArrayList
            Dim Dr(n) As Double
            i = 0
            Do
                Dr(i) = (alpha(i) - 1) / (alpha(lki) - 1) * D * xd(lki) / (F * z(lki)) + (alpha(lki) - alpha(i)) / (alpha(lki) - 1) * D * xd(hki) / (F * z(hki))
                If Dr(i) > 0 And Dr(i) < 1 And z(i) <> 0 And i <> lki And i <> hki Then
                    mode2 = True
                    count += 1
                    indexes.Add(i)
                End If
                i = i + 1
            Loop Until i = n + 1

            If mode2 = False Then

                Dim teta, L_Dmin, sum As Double

                teta = brentsolver.BrentOpt(alpha(hki) * 1.01, alpha(lki), 10, 0.0000001, 100, New Object() {alpha, z, q, n})

                sum = 0
                i = 0
                Do
                    If z(i) <> 0 Then sum += alpha(i) * xd(i) / (alpha(i) - teta)
                    i = i + 1
                Loop Until i = n + 1

                L_Dmin = sum - 1

                m_Rmin = L_Dmin

            Else

                Dim teta(count), xdm(count - 1) As Double

                i = 0
                Do
                    If i = 0 Then
                        teta(i) = brentsolver.BrentOpt(alpha(lki), alpha(indexes(i)), 10, 0.0001, 100, New Object() {alpha, z, q, n})
                    ElseIf i = count Then
                        teta(i) = brentsolver.BrentOpt(alpha(indexes(i - 1)), alpha(hki), 10, 0.0001, 100, New Object() {alpha, z, q, n})
                    Else
                        teta(i) = brentsolver.BrentOpt(alpha(indexes(i - 1)), alpha(indexes(i)), 10, 0.0001, 100, New Object() {alpha, z, q, n})
                    End If
                    i = i + 1
                Loop Until i = count + 1

                Dim MA As New Mapack.Matrix(count, count)
                Dim MB As New Mapack.Matrix(count, 1)
                Dim MX As New Mapack.Matrix(count, count)

                Dim j As Integer = 0
                i = 0
                Do
                    MB(i, 0) = 0
                    j = 0
                    Do
                        If j = 0 Then
                            MA(i, j) = 1 'L/D min
                        Else
                            MA(i, j) = -alpha(indexes(j)) / (alpha(indexes(j)) - teta(i))
                        End If
                        j = j + 1
                    Loop Until j = count
                    j = 0
                    Do
                        If j <> indexes(j) Then
                            MB(i, 0) += alpha(j) * xd(j) / (alpha(j) - teta(i))
                        End If
                        j = j + 1
                    Loop Until j >= count
                    MB(i, 0) -= 1
                    i = i + 1
                Loop Until i >= count

                MX = MA.Solve(MB)

                m_Rmin = MX(0, 0)

            End If

            'actual number of stages by Gilliland's method

            Dim xx, yy As Double
            xx = (R - m_Rmin) / (R + 1)
            yy = 0.75 * (1 - xx ^ 0.5668)
            m_N = (yy + m_Nmin) / (1 - yy)

            'temperatures and heat duties - copy compositions

            Dim Dmw, Bmw As Double

            i = 0
            Dmw = 0
            For Each comp As Interfaces.ICompound In distillate.Phases(0).Compounds.Values
                If Double.IsNaN(xd(i)) = False Then comp.MoleFraction = xd(i) Else comp.MoleFraction = 0
                Dmw += comp.MoleFraction.GetValueOrDefault * comp.ConstantProperties.Molar_Weight
                i = i + 1
            Next
            With distillate.Phases(0)
                .Properties.pressure = Me.m_condenserpressure
                .Properties.molarflow = D
                .Properties.massflow = Dmw * D / 1000
            End With

            i = 0
            Bmw = 0
            For Each comp As Interfaces.ICompound In bottoms.Phases(0).Compounds.Values
                If Double.IsNaN(xb(i)) = False Then comp.MoleFraction = xb(i) Else comp.MoleFraction = 0
                Bmw += comp.MoleFraction.GetValueOrDefault * comp.ConstantProperties.Molar_Weight
                i = i + 1
            Next
            With bottoms.Phases(0)
                .Properties.pressure = Me.m_boilerpressure
                .Properties.molarflow = B
                .Properties.massflow = Bmw * B / 1000
            End With

            Dim result As Object
            Dim TD, TB, TF, HF, HD, HD0, HB, HL As Double
            Dim pp As PropertyPackages.PropertyPackage = Me.PropertyPackage

            TF = feed.Phases(0).Properties.temperature
            HF = feed.Phases(0).Properties.enthalpy.GetValueOrDefault * feed.Phases(0).Properties.molecularWeight.GetValueOrDefault

            pp.CurrentMaterialStream = distillate

            For Each comp As Interfaces.ICompound In distillate.Phases(0).Compounds.Values
                comp.MassFraction = pp.AUX_CONVERT_MOL_TO_MASS(comp.Name, 0)
            Next

            If Me.condtype = CondenserType.PartialCond Then
                result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_condenserpressure, 1, 0)
                TD = result(2)
            ElseIf Me.condtype = CondenserType.TotalCond Then
                result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_condenserpressure, 0, 0)
                TD = result(2)
            End If
            With distillate.Phases(0)
                .Properties.temperature = TD
            End With
            CalculateMaterialStream(FlowSheet, distillate, , True)

            HD = distillate.Phases(0).Properties.enthalpy.GetValueOrDefault * distillate.Phases(0).Properties.molecularWeight.GetValueOrDefault

            pp.CurrentMaterialStream = bottoms

            For Each comp As Interfaces.ICompound In bottoms.Phases(0).Compounds.Values
                comp.MassFraction = pp.AUX_CONVERT_MOL_TO_MASS(comp.Name, 0)
            Next

            result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_boilerpressure, 0.001, 0)
            TB = result(2)
            With bottoms.Phases(0)
                .Properties.temperature = TB
            End With
            CalculateMaterialStream(FlowSheet, bottoms, , True)

            HB = bottoms.Phases(0).Properties.enthalpy.GetValueOrDefault * bottoms.Phases(0).Properties.molecularWeight.GetValueOrDefault

            pp.CurrentMaterialStream = distillate
            If Me.condtype = CondenserType.PartialCond Then
                result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_condenserpressure, 0, 0)
                HL = result(4) * distillate.Phases(0).Properties.molecularWeight.GetValueOrDefault
                m_Qc = -(HL - HD) * L / 1000
            ElseIf Me.condtype = CondenserType.TotalCond Then
                result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_condenserpressure, 1, 0)
                HD0 = result(4) * distillate.Phases(0).Properties.molecularWeight.GetValueOrDefault
                result = pp.DW_CalcEquilibrio_ISOL(PropertyPackages.FlashSpec.P, PropertyPackages.FlashSpec.VAP, m_condenserpressure, 0, 0)
                HL = result(4) * distillate.Phases(0).Properties.molecularWeight.GetValueOrDefault
                m_Qc = -(HL - HD0) * (L + D) / 1000
            End If

            m_Qb = D / 1000 * HD + B / 1000 * HB + m_Qc - F / 1000 * HF

            'optimum feed stage by Fenske's method

            Dim NminS, Ss, Ns As Double

            Ss = z(lki) / z(hki) * xb(hki) / xb(lki)
            'alpha_s = bottoms.Phases(2).Compounds(m_lightkey).MoleFraction.GetValueOrDefault / bottoms.Phases(1).Compounds(m_lightkey).MoleFraction.GetValueOrDefault
            'alpha_s = alpha_s / (bottoms.Phases(2).Compounds(m_heavykey).MoleFraction.GetValueOrDefault / bottoms.Phases(1).Compounds(m_heavykey).MoleFraction.GetValueOrDefault)
            NminS = Log(Ss) / Log(alpha(lki))
            Ns = NminS * m_N / m_Nmin
            ofs = Ns

            'update exchanger duties

            With cduty
                .EnergyFlow = m_Qc
                .GraphicObject.Calculated = True
            End With

            With rduty
                .EnergyFlow = m_Qb
                .GraphicObject.Calculated = True
            End With

            'call the flowsheet calculation routine

            With objargs
                .Calculated = True
                .Name = Me.Name
                .ObjectType = Me.GraphicObject.ObjectType
            End With

final:      FlowSheet.CalculationQueue.Enqueue(objargs)

        End Function

        Function rminfunc(ByVal x As Double, ByVal otherargs As Object) As Double

            If Double.IsNaN(x) Then Exit Function

            Dim alpha As Object = otherargs(0)
            Dim z As Object = otherargs(1)
            Dim q As Double = otherargs(2)
            Dim n As Integer = otherargs(3)

            Dim value As Double
            Dim j As Integer = 0
            Do
                If z(j) <> 0 Then value += (alpha(j) * z(j)) / (alpha(j) - x)
                j = j + 1
            Loop Until j = n + 1

            CheckCalculatorStatus()

            Return value - 1 + q

        End Function

        Public Overrides Function DeCalculate() As Integer

            If Me.GraphicObject.OutputConnectors(0).IsAttached Then

                'Zerar valores da corrente de matéria conectada a jusante
                DirectCast(FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Name), MaterialStream).Clear()

            End If

            If Me.GraphicObject.OutputConnectors(1).IsAttached Then

                'Zerar valores da corrente de matéria conectada a jusante
                DirectCast(FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.OutputConnectors(1).AttachedConnector.AttachedTo.Name), MaterialStream).Clear()

            End If

            If Me.GraphicObject.EnergyConnector.IsAttached Then

                Dim cduty As SimulationObjects.Streams.EnergyStream = FlowSheet.Collections.FlowsheetObjectCollection(Me.GraphicObject.EnergyConnector.AttachedConnector.AttachedTo.Name)

                With cduty
                    .EnergyFlow = Nothing
                    .GraphicObject.Calculated = False
                End With

            End If

            'Call function to calculate flowsheet
            Dim objargs As New DWSIM.Extras.StatusChangeEventArgs
            With objargs
                .Calculated = False
                .Name = Me.Name
                .Tag = Me.GraphicObject.Tag
                .ObjectType = Me.GraphicObject.ObjectType
            End With

            FlowSheet.CalculationQueue.Enqueue(objargs)

        End Function

        Public Overrides Sub PopulatePropertyGrid(ByVal pgrid As PropertyGridEx.PropertyGridEx, ByVal su As SystemsOfUnits.Units)
            Dim Conversor As New SystemsOfUnits.Converter

            With pgrid

                .PropertySort = PropertySort.Categorized
                .ShowCustomProperties = True
                .Item.Clear()

                MyBase.PopulatePropertyGrid(pgrid, su)

                Dim ent, saida1, saida2, ec, er As String
                If Me.GraphicObject.InputConnectors(0).IsAttached = True Then
                    ent = Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Tag
                Else
                    ent = ""
                End If
                If Me.GraphicObject.OutputConnectors(0).IsAttached = True Then
                    saida1 = Me.GraphicObject.OutputConnectors(0).AttachedConnector.AttachedTo.Tag
                Else
                    saida1 = ""
                End If
                If Me.GraphicObject.OutputConnectors(1).IsAttached = True Then
                    saida2 = Me.GraphicObject.OutputConnectors(1).AttachedConnector.AttachedTo.Tag
                Else
                    saida2 = ""
                End If
                If Me.GraphicObject.InputConnectors(1).IsAttached = True Then
                    er = Me.GraphicObject.InputConnectors(1).AttachedConnector.AttachedFrom.Tag
                Else
                    er = ""
                End If
                If Me.GraphicObject.EnergyConnector.IsAttached = True Then
                    ec = Me.GraphicObject.EnergyConnector.AttachedConnector.AttachedTo.Tag
                Else
                    ec = ""
                End If

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCFeed"), ent, False, Me.FlowSheet.GetTranslatedString("Conexes1"), "", True)
                With .Item(.Item.Count - 1)
                    .DefaultValue = Nothing
                    .CustomEditor = New DWSIM.Editors.Streams.UIInputMSSelector
                End With

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCDistillate"), saida1, False, Me.FlowSheet.GetTranslatedString("Conexes1"), "", True)
                With .Item(.Item.Count - 1)
                    .DefaultValue = Nothing
                    .CustomEditor = New DWSIM.Editors.Streams.UIOutputMSSelector
                End With

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCBottoms"), saida2, False, Me.FlowSheet.GetTranslatedString("Conexes1"), "", True)
                With .Item(.Item.Count - 1)
                    .DefaultValue = Nothing
                    .CustomEditor = New DWSIM.Editors.Streams.UIOutputMSSelector
                End With

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCCondenserDuty"), ec, False, Me.FlowSheet.GetTranslatedString("Conexes1"), "", True)
                With .Item(.Item.Count - 1)
                    .DefaultValue = Nothing
                    .CustomEditor = New DWSIM.Editors.Streams.UIOutputESSelector
                End With

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCReboilerDuty"), er, False, Me.FlowSheet.GetTranslatedString("Conexes1"), "", True)
                With .Item(.Item.Count - 1)
                    .DefaultValue = Nothing
                    .CustomEditor = New DWSIM.Editors.Streams.UIInputESSelector
                End With

                .Item.Add(Me.FlowSheet.GetTranslatedString("SCCondenserType"), Me, "condtype", False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCCondenserType"), True)
                .Item(.Item.Count - 1).Tag2 = "condtype"
                .Item.Add(Me.FlowSheet.GetTranslatedString("SCRefluxRatio"), Me, "m_refluxratio", False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCRefluxRatio"), True)
                .Item(.Item.Count - 1).Tag2 = "PROP_SC_0"
                .Item.Add(Me.FlowSheet.GetTranslatedString("SCLightKey"), DWSIM.App.GetComponentName(Me.m_lightkey), False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCLightKeyMF"), True)
                With .Item(.Item.Count - 1)
                    .IsBrowsable = False
                    .CustomEditor = New DWSIM.Editors.Components.UIComponentSelector
                    .DefaultValue = ""
                    .DefaultType = GetType(String)
                End With
                .Item.Add(Me.FlowSheet.GetTranslatedString("SCLightKeyMF"), Me, "m_lightkeymolarfrac", False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCLightKeyMF"), True)
                .Item(.Item.Count - 1).Tag2 = "PROP_SC_2"
                .Item.Add(Me.FlowSheet.GetTranslatedString("SCHeavyKey"), DWSIM.App.GetComponentName(Me.m_heavykey), False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCHeavyKey"), True)
                With .Item(.Item.Count - 1)
                    .IsBrowsable = False
                    .CustomEditor = New DWSIM.Editors.Components.UIComponentSelector
                    .DefaultValue = ""
                    .DefaultType = GetType(String)
                End With
                .Item.Add(Me.FlowSheet.GetTranslatedString("SCHeavyKeyMF"), Me, "m_heavykeymolarfrac", False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCHeavyKeyMF"), True)
                .Item(.Item.Count - 1).Tag2 = "PROP_SC_1"
                Dim valor = Format(SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.m_condenserpressure), FlowSheet.Options.NumberFormat)
                .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCCondenserPressure"), su.pressure), Double.Parse(valor), False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCCondenserPressure"), True)
                With .Item(.Item.Count - 1)
                    .CustomTypeConverter = New System.ComponentModel.StringConverter
                    .Tag2 = "PROP_SC_3"
                    .Tag = New Object() {FlowSheet.Options.NumberFormat, su.pressure, "P"}
                    .CustomEditor = New DWSIM.Editors.Generic.UIUnitConverter
                End With
                valor = Format(SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.m_boilerpressure), FlowSheet.Options.NumberFormat)
                .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCReboilerPressure"), su.pressure), Double.Parse(valor), False, Me.FlowSheet.GetTranslatedString("Parmetros2"), Me.FlowSheet.GetTranslatedString("SCReboilerPressure"), True)
                With .Item(.Item.Count - 1)
                    .CustomTypeConverter = New System.ComponentModel.StringConverter
                    .Tag2 = "PROP_SC_4"
                    .Tag = New Object() {FlowSheet.Options.NumberFormat, su.pressure, "P"}
                    .CustomEditor = New DWSIM.Editors.Generic.UIUnitConverter
                End With

                If Me.GraphicObject.Calculated Then

                    .Item.Add(Me.FlowSheet.GetTranslatedString("SCMinimumRefluxRatio"), Format(Me.m_Rmin, FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCMinimumRefluxRatio"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(Me.FlowSheet.GetTranslatedString("SCNminstages"), Format(Me.m_Nmin, FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCNminstages"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(Me.FlowSheet.GetTranslatedString("SCNstages"), Format(Me.m_N, FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCNstages"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(Me.FlowSheet.GetTranslatedString("SCOptimalFeedStage"), Format(Me.ofs, FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCOptimalFeedStage"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCStrippingLiquid"), su.molarflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.L_), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCStrippingLiquid"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCRectifyLiquid"), su.molarflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.L), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCRectifyLiquid"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCStrippingVapor"), su.molarflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.V_), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCStrippingVapor"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCRectifyVapor"), su.molarflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.V), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCRectifyVapor"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCCondenserDuty"), su.heatflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.m_Qc), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCCondenserDuty"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With
                    .Item.Add(FT(Me.FlowSheet.GetTranslatedString("SCReboilerDuty"), su.heatflow), Format(SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.m_Qb), FlowSheet.Options.NumberFormat), True, Me.FlowSheet.GetTranslatedString("Resultados3"), Me.FlowSheet.GetTranslatedString("SCReboilerDuty"), True)
                    With .Item(.Item.Count - 1)
                        .DefaultValue = Nothing
                        .DefaultType = GetType(Nullable(Of Double))
                    End With

                End If

                .ExpandAllGridItems()

            End With

        End Sub

        Public Overrides Function GetPropertyValue(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Object

            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim value As Double = 0
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_SC_0	Reflux Ratio
                    value = Me.m_refluxratio
                Case 1
                    'PROP_SC_1	Heavy Key Molar Fraction
                    value = Me.m_heavykeymolarfrac
                Case 2
                    'PROP_SC_2	Light Key Molar Fraction
                    value = Me.m_lightkeymolarfrac
                Case 3
                    'PROP_SC_3	Condenser Pressure
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.m_condenserpressure)
                Case 4
                    'PROP_SC_4	Reboiler Pressure
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.m_boilerpressure)
                Case 5
                    'PROP_SC_5	Minimun Reflux Ratio
                    value = Me.m_Rmin
                Case 6
                    'PROP_SC_6	Minimum Stages
                    value = Me.m_Nmin
                Case 7
                    'PROP_SC_7	Optimal Feed Stage
                    value = Me.ofs
                Case 8
                    'PROP_SC_8	Stripping Liquid Molar Flow
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.L_)
                Case 9
                    'PROP_SC_9	Rectify Liquid Molar Flow
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.L)
                Case 10
                    'PROP_SC_10	Stripping Vapor Molar Flow
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.V_)
                Case 11
                    'PROP_SC_11	Rectify Vapor Molar Flow
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.molarflow, Me.V)
                Case 12
                    'PROP_SC_12	Condenser Duty
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.m_Qc)
                Case 13
                    'PROP_SC_13	Reboiler Duty
                    value = SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.m_Qb)
            End Select

            Return value

        End Function


        Public Overloads Overrides Function GetProperties(ByVal proptype As Interfaces.Enums.PropertyType) As String()
            Dim i As Integer = 0
            Dim proplist As New ArrayList
            Select Case proptype
                Case PropertyType.RO
                    For i = 5 To 13
                        proplist.Add("PROP_SC_" + CStr(i))
                    Next
                Case PropertyType.RW
                    For i = 0 To 13
                        proplist.Add("PROP_SC_" + CStr(i))
                    Next
                Case PropertyType.WR
                    For i = 0 To 4
                        proplist.Add("PROP_SC_" + CStr(i))
                    Next
                Case PropertyType.ALL
                    For i = 0 To 13
                        proplist.Add("PROP_SC_" + CStr(i))
                    Next
            End Select
            Return proplist.ToArray(GetType(System.String))
            proplist = Nothing
        End Function

        Public Overrides Function SetPropertyValue(ByVal prop As String, ByVal propval As Object, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Boolean
            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_SC_0	Reflux Ratio
                    Me.m_refluxratio = propval
                Case 1
                    'PROP_SC_1	Heavy Key Molar Fraction
                    Me.m_heavykeymolarfrac = propval
                Case 2
                    'PROP_SC_2	Light Key Molar Fraction
                    Me.m_lightkeymolarfrac = propval
                Case 3
                    'PROP_SC_3	Condenser Pressure
                    Me.m_condenserpressure = SystemsOfUnits.Converter.ConvertToSI(su.pressure, propval)
                Case 4
                    'PROP_SC_4	Reboiler Pressure
                    Me.m_boilerpressure = SystemsOfUnits.Converter.ConvertToSI(su.pressure, propval)

            End Select
            Return 1
        End Function

        Public Overrides Function GetPropertyUnit(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As String
            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim value As String = ""
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_SC_0	Reflux Ratio
                    value = ""
                Case 1
                    'PROP_SC_1	Heavy Key Molar Fraction
                    value = ""
                Case 2
                    'PROP_SC_2	Light Key Molar Fraction
                    value = ""
                Case 3
                    'PROP_SC_3	Condenser Pressure
                    value = su.pressure
                Case 4
                    'PROP_SC_4	Reboiler Pressure
                    value = su.pressure
                Case 5
                    'PROP_SC_5	Minimun Reflux Ratio
                    value = ""
                Case 6
                    'PROP_SC_6	Minimum Stages
                    value = ""
                Case 7
                    'PROP_SC_7	Optimal Feed Stage
                    value = ""
                Case 8
                    'PROP_SC_8	Stripping Liquid Molar Flow
                    value = su.molarflow
                Case 9
                    'PROP_SC_9	Rectify Liquid Molar Flow
                    value = su.molarflow
                Case 10
                    'PROP_SC_10	Stripping Vapor Molar Flow
                    value = su.molarflow
                Case 11
                    'PROP_SC_11	Rectify Vapor Molar Flow
                    value = su.molarflow
                Case 12
                    'PROP_SC_12	Condenser Duty
                    value = su.heatflow
                Case 13
                    'PROP_SC_13	Reboiler Duty
                    value = su.heatflow
            End Select

            Return value
        End Function
    End Class

End Namespace


