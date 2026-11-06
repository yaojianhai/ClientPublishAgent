!ifndef SCRIPTDIR
    !define SCRIPTDIR "${__FILEDIR__}"
!endif
!ifndef PRODUCT_VERSION
    !error "必须由打包脚本提供 PRODUCT_VERSION。"
!endif
!ifndef ReleaseFile
    !error "必须由打包脚本提供 ReleaseFile。"
!endif
!ifndef OutPutFile
    !error "必须由打包脚本提供 OutPutFile。"
!endif
!ifndef ISREMOVEUSERS
    !define ISREMOVEUSERS 0
!endif
!addincludedir "${SCRIPTDIR}\NSIS\Include"
!addplugindir "${SCRIPTDIR}\NSIS\Plugins"
!include "${NSISDIR}\Include\WordFunc.nsh"
Var BGImage
Var ImageHandle
Var BGImage1
Var ImageHandle1
Var SlideParent
Var Txt_Browser
Var btn_Browser
Var btn_in
Var btn_ins
Var btn_back
Var btn_instetup
Var btn_instend
Var btn_instend1
Var btn_Licenseback
Var Txt_Xllicense
Var Rtf_license
Var Txt_ji
Var Ckbox0
!define PRODUCT_NAME "Warrenq F1"
!define PRODUCT_EXE "WarrenqF1.Desktop.exe"
!define PRODUCT_INSTALL_DIR "WarrenqF1"
!define COMPANYCODE "GILDATA"
!define PRODUCT_PUBLISHER "${COMPANYCODE}, Inc."
!define PRODUCT_WEB_SITE "https://www.gildata.com"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${COMPANYCODE}WarrenqF1"
!define PRODUCT_DIR_REGKEY "Software\${COMPANYCODE}\WarrenqF1"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"
!define SETUPDIR "${SCRIPTDIR}\image"
SetCompressor lzma
SetCompress force
XPStyle on
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${OutPutFile}"
RequestExecutionLevel admin
InstallDir "C:\${COMPANYCODE}\${PRODUCT_INSTALL_DIR}"
!define DIR "C:\${COMPANYCODE}\${PRODUCT_INSTALL_DIR}"
InstallDirRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "InstallLocation"
ShowInstDetails nevershow
ShowUnInstDetails nevershow
!define MUI_ICON "${SETUPDIR}\warrenq.ico"
!define MUI_UNICON "${SETUPDIR}\warrenq.ico"
ReserveFile "${SETUPDIR}\bg.bmp"
ReserveFile "${SETUPDIR}\bg2.bmp"
ReserveFile "${SETUPDIR}\bg3.bmp"
ReserveFile "${SETUPDIR}\Slides.dat"
ReserveFile "${SETUPDIR}\InstallingBG01.png"
ReserveFile "${SETUPDIR}\InstallingBG02.png"
ReserveFile "${SETUPDIR}\InstallingBG03.png"
ReserveFile "${SETUPDIR}\InstallingBG04.png"
ReserveFile "${SCRIPTDIR}\NSIS\Plugins\nsisSlideshow.dll"
!define MUI_UI "${SCRIPTDIR}\NSIS\ui\mod.exe"
!include "${NSISDIR}\Include\MUI.nsh"
!include "${NSISDIR}\Include\nsDialogs.nsh"
!include "${NSISDIR}\Include\LogicLib.nsh"
!ifndef GWL_STYLE
    !define GWL_STYLE -16
!endif
!ifndef WS_EX_STATICEDGE
    !define WS_EX_STATICEDGE 0x00020000
!endif
!ifndef ES_READONLY
    !define ES_READONLY 0x0800
!endif
!ifndef WS_VISIBLE
    !define WS_VISIBLE 0x10000000
!endif
!ifndef WS_CHILD
    !define WS_CHILD 0x40000000
!endif
!ifndef WS_TABSTOP
    !define WS_TABSTOP 0x00010000
!endif
!ifndef WS_VSCROLL
    !define WS_VSCROLL 0x00200000
!endif
!ifndef ES_MULTILINE
    !define ES_MULTILINE 0x0004
!endif
!ifndef ES_WANTRETURN
    !define ES_WANTRETURN 0x1000
!endif
!ifndef SS_BITMAP
    !define SS_BITMAP 0x000E
!endif
!ifndef STM_SETIMAGE
    !define STM_SETIMAGE 0x0172
!endif
!ifndef IMAGE_BITMAP
    !define IMAGE_BITMAP 0
!endif
!include "${NSISDIR}\Include\WinMessages.nsh"
!include "LoadRTF.nsh"
!macro NSW_SetWindowSize hwnd width height
    System::Call "user32::MoveWindow(i ${hwnd}, i 0, i 0, i ${width}, i ${height}, i 1)"
!macroend
!define NSW_SetWindowSize `!insertmacro NSW_SetWindowSize`
!macro NSW_CenterMainWindow hwnd width height
    System::Call "user32::GetWindowLong(p ${hwnd}, i ${GWL_STYLE}) p.r0"
    System::Call "user32::GetWindowLong(p ${hwnd}, i ${GWL_EXSTYLE}) p.r1"
    System::Call "*(i 0, i 0, i ${width}, i ${height}) p.r2"
    System::Call "user32::AdjustWindowRectEx(p r2, p r0, i 0, p r1)"
    System::Call "*$2(i.r3, i.r4, i.r5, i.r6)"
    IntOp $3 $5 - $3
    IntOp $4 $6 - $4
    System::Call "user32::GetSystemMetrics(i 0) i.r5"
    System::Call "user32::GetSystemMetrics(i 1) i.r6"
    IntOp $5 $5 - $3
    IntOp $5 $5 / 2
    IntOp $6 $6 - $4
    IntOp $6 $6 / 2
    System::Call "user32::SetWindowPos(p ${hwnd}, p 0, i r5, i r6, i r3, i r4, u 0x0014)"
!macroend
!define NSW_CenterMainWindow `!insertmacro NSW_CenterMainWindow`
!include "${NSISDIR}\Include\x64.nsh"
!include "${NSISDIR}\Include\Library.nsh"
!define LIBRARY_X64
!define MUI_CUSTOMFUNCTION_GUIINIT onGUIInit
Page custom Page.1
!define MUI_PAGE_CUSTOMFUNCTION_SHOW InstFilesPageShow
!insertmacro MUI_PAGE_INSTFILES
Page custom Page.3
Page custom Page.4
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
VIProductVersion "1.0.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "Comments" "${PRODUCT_WEB_SITE}"
VIAddVersionKey /LANG=2052 "CompanyName" "${COMPANYCODE}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright (c) ${COMPANYCODE}"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "FileVersion" "${PRODUCT_VERSION}"
Function .onInit
    InitPluginsDir
    File "/ONAME=$PLUGINSDIR\bg.bmp" "${SETUPDIR}\bg.bmp"
    File "/oname=$PLUGINSDIR\bg2.bmp" "${SETUPDIR}\bg2.bmp"
    File "/oname=$PLUGINSDIR\bg3.bmp" "${SETUPDIR}\bg3.bmp"
    File "/oname=$PLUGINSDIR\Slides.dat" "${SETUPDIR}\Slides.dat"
    File "/oname=$PLUGINSDIR\InstallingBG01.png" "${SETUPDIR}\InstallingBG01.png"
    File "/oname=$PLUGINSDIR\InstallingBG02.png" "${SETUPDIR}\InstallingBG02.png"
    File "/oname=$PLUGINSDIR\InstallingBG03.png" "${SETUPDIR}\InstallingBG03.png"
    File "/oname=$PLUGINSDIR\InstallingBG04.png" "${SETUPDIR}\InstallingBG04.png"
    File "/oname=$PLUGINSDIR\license.rtf" "${SETUPDIR}\license.rtf"
FunctionEnd
Function onGUIInit
    ReadRegStr $0 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "InstallLocation"
    ${If} $0 == ''
        ReadRegStr $1 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString"
        StrCpy $0 $1 -11
    ${EndIf}
    ${If} $0 != ''
        SetShellVarContext all
        MessageBox MB_OKCANCEL "检测到已安装的 Warrenq F1。是否先卸载旧版本？" IDOK CONFIRM1 IDCANCEL Exit
        CONFIRM1:
        ExecWait '$0\uninst.exe _?=$0'
    ${EndIf}
    Push $R0
    FINDEXCEL:
    Push "EXCEL.EXE"
    ProcessWork::existsprocess
    Pop $R0
    IntCmp $R0 0 CheckProc
    MessageBox MB_RETRYCANCEL "请关闭 Excel 后重试。" IDRETRY FINDEXCEL IDCANCEL Exit
    Push "EXCEL.EXE"
    Processwork::KillProcess
    Push $R0
    CheckProc:
    Push "WarrenqF1.Desktop.exe"
    ProcessWork::existsprocess
    Pop $R0
    IntCmp $R0 0 Done
    MessageBox MB_OKCANCEL "检测到 Warrenq F1 正在运行。是否关闭相关程序并继续？" IDCANCEL Exit
    Push "WarrenqF1.Desktop.exe"
    ProcessWork::KillProcess
    Goto Done
    Exit:
    Abort
    Done:
    Pop $R0
    System::Call "user32::SetWindowLong(p $HWNDPARENT, i ${GWL_STYLE}, p 0x10CA0000)"
    GetDlgItem $0 $HWNDPARENT 1034
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1035
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1036
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1037
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1038
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1039
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1256
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1028
    ShowWindow $0 ${SW_HIDE}
    ${NSW_CenterMainWindow} $HWNDPARENT 588 438
FunctionEnd
Function Page.1
    GetDlgItem $0 $HWNDPARENT 1
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 2
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 3
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1990
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1991
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 1992
    ShowWindow $0 ${SW_HIDE}
    nsDialogs::Create 1044
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}
    SetCtlColors $0 ""  transparent
    ${NSW_SetWindowSize} $0 588 438
    nsDialogs::CreateControl "RichEdit20W" ${ES_READONLY}|${WS_VISIBLE}|${WS_CHILD}|${WS_TABSTOP}|${WS_VSCROLL}|${ES_MULTILINE}|${ES_WANTRETURN} ${WS_EX_STATICEDGE} 16u 28u 360u 229u ''
    Pop $rtf_License
    ${LoadRTF} '$PLUGINSDIR\license.rtf' $rtf_License
    ShowWindow $rtf_License ${SW_HIDE}
    ${NSD_CreateButton} 180u 263u 62 26 "返回"
    Pop $btn_Licenseback
    ${NSD_SetText} $btn_Licenseback "返回"
    ${NSD_OnClick} $btn_Licenseback Licenseback
    SetCtlColors $btn_Licenseback 7F7F7F transparent
    ShowWindow $btn_Licenseback ${SW_HIDE}
    ${NSD_CreateButton} 488 402 86 26 "自定义安装"
    Pop $btn_ins
    ${NSD_SetText} $btn_ins "自定义安装"
    ${NSD_OnClick} $btn_ins onClickint
    ${NSD_CreateButton} 209 300 170 42 "立即安装"
    Pop $btn_in
    ${NSD_SetText} $btn_in "立即安装"
    ${NSD_OnClick} $btn_in onClickins
    ${NSD_CreateButton} 450 402 62 26 "安装"
    Pop $btn_instetup
    ${NSD_SetText} $btn_instetup "安装"
    ${NSD_OnClick} $btn_instetup onClickins
    SetCtlColors $btn_instetup FFFFFF transparent
    ShowWindow $btn_instetup ${SW_HIDE}
    ${NSD_CreateButton} 518 402 62 26 "返回"
    Pop $btn_back
    ${NSD_SetText} $btn_back "返回"
    ${NSD_OnClick} $btn_back onClickBack
    SetCtlColors $btn_back 7F7F7F transparent
    ShowWindow $btn_back ${SW_HIDE}
    ${NSD_CreateCheckbox} 40 404 20 18 " "
    Pop $Ckbox0
    SetCtlColors $Ckbox0 "" FFFFFF
    ${NSD_Check} $Ckbox0
    ${NSD_OnClick} $Ckbox0 Chklicense
    ${NSD_CreateLabel} 60 407 88 18 "已阅读并接受"
    Pop $txt_ji
    SetCtlColors $txt_ji 256DFF  FFFFFF
    ${NSD_CreateLink} 155 407 260 18 "恒生聚源服务条款"
    Pop $Txt_Xllicense
    SetCtlColors $Txt_Xllicense 256DFF FFFFFF
    ${NSD_OnClick} $Txt_Xllicense xllicense
    ${NSD_CreateText} 75 310 363 25 "${DIR}"
    Pop $Txt_Browser
    SetCtlColors $Txt_Browser ""  FFFFFF
    ${NSD_AddExStyle} $Txt_Browser ${WS_EX_WINDOWEDGE}
    CreateFont $1 "tahoma" "10" "500"
    SendMessage $Txt_Browser ${WM_SETFONT} $1 1
    ShowWindow $Txt_Browser ${SW_HIDE}
    ${NSD_CreateButton} 450 310 62 26 "浏览"
    Pop $btn_Browser
    ${NSD_SetText} $btn_Browser "浏览"
    ${NSD_OnClick} $btn_Browser onClickSelectPath
    ShowWindow $btn_Browser ${SW_HIDE}
    ${NSD_CreateBitmap} 0 0 100% 100% ""
    Pop $BGImage1
    ${NSD_SetImage} $BGImage1 $PLUGINSDIR\bg2.bmp $ImageHandle1
    ShowWindow $BGImage1 ${SW_HIDE}
    ${NSD_CreateBitmap} 0 0 100% 100% ""
    Pop $BGImage
    ${NSD_SetImage} $BGImage $PLUGINSDIR\bg.bmp $ImageHandle
    nsDialogs::Show
    ${NSD_FreeImage} $ImageHandle
    ${NSD_FreeImage} $ImageHandle1
FunctionEnd
Function  InstFilesPageShow
    FindWindow $R2 "#32770" "" $HWNDPARENT
    StrCpy $SlideParent $R2
    GetDlgItem $1 $R2 1027
    ShowWindow $1 ${SW_HIDE}
    StrCpy $R0 $R2
    System::Call "user32::MoveWindow(i R0, i 0, i 0, i 588, i 438) i r2"
    GetDlgItem $R0 $R2 1004
    System::Call "user32::MoveWindow(i R0, i 21, i 380, i 537, i 12) i r2"
    GetDlgItem $R1 $R2 1006
    SetCtlColors $R1 000000 transparent
    SendMessage $R1 ${WM_SETTEXT} 0 "STR:正在安装..."
    ShowWindow $R1 ${SW_SHOW}
    System::Call "user32::MoveWindow(i R1, i 21, i 350, i 290, i 12) i r2"
    GetDlgItem $R8 $R2 1016
    ShowWindow $R8 ${SW_HIDE}
    System::Call "user32::MoveWindow(i R8, i 0, i 0, i 588, i 326) i r2"
    GetDlgItem $R0 $R2 1995
    System::Call "user32::MoveWindow(i R0, i 0, i 0, i 498, i 373) i r2"
    ${NSD_SetImage} $R0 $PLUGINSDIR\bg2.bmp $ImageHandle
    FindWindow $R2 "#32770" "" $HWNDPARENT
    GetDlgItem $5 $R2 1004
    ShowWindow $5 ${SW_SHOW}
FunctionEnd
Function Page.3
    GetDlgItem $0 $HWNDPARENT 1
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 2
    ShowWindow $0 ${SW_HIDE}
    GetDlgItem $0 $HWNDPARENT 3
    ShowWindow $0 ${SW_HIDE}
    nsDialogs::Create 1044
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}
    SetCtlColors $0 ""  transparent
    ${NSW_SetWindowSize} $0 588 438
    ${NSD_CreateButton} 105 363 172 42 "立即体验"
    Pop $btn_instend
    ${NSD_SetText} $btn_instend "立即体验"
    ${NSD_OnClick} $btn_instend onClickexpress
    ${NSD_CreateButton} 200u 363 172 42 "完成"
    Pop $btn_instend1
    ${NSD_SetText} $btn_instend1 "完成"
    ${NSD_OnClick} $btn_instend1 onClickend
    ${NSD_CreateBitmap} 0 0 100% 100% ""
    Pop $BGImage
    ${NSD_SetImage} $BGImage $PLUGINSDIR\bg3.bmp $ImageHandle
    nsDialogs::Show
    ${NSD_FreeImage} $ImageHandle
FunctionEnd
Function Page.4
FunctionEnd
Section MainSetup
    nsisSlideshow::Show /NOUNLOAD /hwnd=$SlideParent /auto=$PLUGINSDIR\Slides.dat
    SetDetailsPrint none
    Sleep 500
    SetOutPath $INSTDIR
    SetOverwrite on
    SetShellVarContext all
    ${IF} ${RunningX64}
        File /r /x *.svn "${ReleaseFile}\*.*"
    ${Else}
        File /r /x Win64 /x *.svn "${ReleaseFile}\*.*"
    ${EndIf}
    File "${SETUPDIR}\warrenq.ico"
    CreateDirectory "$PROFILE\.warrenqai"
    SetOutPath $INSTDIR
    Sleep 100
    nsExec::Exec '"$INSTDIR\7za.exe" x "$INSTDIR\WarrenqAIEngine\warrenqai\node_modules.7z" -y -o"$INSTDIR\WarrenqAIEngine\warrenqai\node_modules"'
    nsExec::Exec '"$INSTDIR\7za.exe" x "$INSTDIR\WarrenqAIEngine\warrenqai\dist.7z" -y -o"$INSTDIR\WarrenqAIEngine\warrenqai\dist"'
    Sleep 100
    ExecShell "" '"$INSTDIR\WarrenqAIEngine\service\WarrenQAIService.exe"' SW_HIDE
    Sleep 100
    nsExec::ExecToStack '"$INSTDIR\Excel\Excel\RegTool.exe" /register'
    AccessControl::GrantOnFile \
        "$INSTDIR" "everyone" "FullAccess"
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\warrenq.ico"
    CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\warrenq.ico" 0 SW_SHOWNORMAL "" "${PRODUCT_NAME}"
    SetShellVarContext all
    CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk" "$INSTDIR\uninst.exe"
    nsisSlideshow::Stop
    SetAutoClose true
SectionEnd
Section -Post
    WriteUninstaller "$INSTDIR\uninst.exe"
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\${PRODUCT_EXE}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLUpdateInfo" "${PRODUCT_WEB_SITE}"
SectionEnd
Function RelGotoPage
    IntCmp $R9 0 0 Move Move
    StrCmp $R9 "X" 0 Move
    StrCpy $R9 "120"
    Move:
    SendMessage $HWNDPARENT "0x408" "$R9" ""
FunctionEnd
Function onClickins
    ${NSD_GetText} $Txt_Browser  $R0
    ClearErrors
    ${WordFind} $R0 "\" -1 $R1
    StrLen $1 $R1
    ${If} $R1 == '${PRODUCT_INSTALL_DIR}'
    ${ElseIf} $1 == 2
        StrCpy $0 "$0${PRODUCT_INSTALL_DIR}"
    ${Else}
        StrCpy $0 "$0\${PRODUCT_INSTALL_DIR}"
    ${EndIf}
    CreateDirectory "$R0"
    IfErrors 0 +3
    MessageBox MB_ICONINFORMATION|MB_OK "无法创建安装目录：$R0"
    Return
    StrCpy $INSTDIR  $R0
    StrCpy $R9 1
    Call RelGotoPage
    Abort
FunctionEnd
Function onClickint
    ShowWindow $BGImage ${SW_HIDE}
    ShowWindow $Ckbox0 ${SW_HIDE}
    ShowWindow $Txt_Xllicense ${SW_HIDE}
    ShowWindow $Txt_ji ${SW_HIDE}
    ShowWindow $btn_in ${SW_HIDE}
    ShowWindow $btn_ins ${SW_HIDE}
    ShowWindow $BGImage1 ${SW_SHOW}
    ShowWindow $btn_instetup ${SW_SHOW}
    ShowWindow $btn_back ${SW_SHOW}
    ShowWindow $btn_Browser ${SW_SHOW}
    ShowWindow $Txt_Browser ${SW_SHOW}
FunctionEnd
Function onClickBack
    ShowWindow $BGImage1 ${SW_HIDE}
    ShowWindow $BGImage ${SW_SHOW}
    ShowWindow $Ckbox0 ${SW_SHOW}
    ShowWindow $Txt_Xllicense ${SW_SHOW}
    ShowWindow $Txt_ji ${SW_SHOW}
    ShowWindow $btn_in ${SW_SHOW}
    ShowWindow $btn_ins ${SW_SHOW}
    ShowWindow $btn_instetup ${SW_HIDE}
    ShowWindow $btn_back ${SW_HIDE}
    ShowWindow $btn_Browser ${SW_HIDE}
    ShowWindow $Txt_Browser ${SW_HIDE}
FunctionEnd
Function xllicense
    ShowWindow $Ckbox0 ${SW_HIDE}
    ShowWindow $Txt_Xllicense ${SW_HIDE}
    ShowWindow $Txt_ji ${SW_HIDE}
    ShowWindow $btn_in ${SW_HIDE}
    ShowWindow $btn_ins ${SW_HIDE}
    ShowWindow $rtf_License ${SW_SHOW}
    ShowWindow $btn_Licenseback ${SW_SHOW}
FunctionEnd
Function Licenseback
    ShowWindow $Ckbox0 ${SW_SHOW}
    ShowWindow $Txt_Xllicense ${SW_SHOW}
    ShowWindow $Txt_ji ${SW_SHOW}
    ShowWindow $btn_in ${SW_SHOW}
    ShowWindow $btn_ins ${SW_SHOW}
    ShowWindow $btn_Licenseback ${SW_HIDE}
    ShowWindow $rtf_License ${SW_HIDE}
FunctionEnd
Function Chklicense
    Pop $Ckbox0
    ${NSD_GetState} $Ckbox0 $0
    ${If} $0 == 1
        EnableWindow $btn_ins 1
        EnableWindow $btn_in 1
    ${Else}
        EnableWindow $btn_ins 0
        EnableWindow $btn_in 0
    ${EndIf}
FunctionEnd
Function onClickSelectPath
    ${NSD_GetText} $Txt_Browser  $0
    nsDialogs::SelectFolderDialog  "选择 Warrenq F1 安装目录"  "$0"
    Pop $0
    ${IfNot} $0 == error
        ${WordFind} $0 "\" -1 $R1
        StrLen $1 $R1
        ${If} $R1 == '${PRODUCT_INSTALL_DIR}'
        ${ElseIf} $1 == 2
            StrCpy $0 "$0${PRODUCT_INSTALL_DIR}"
        ${Else}
            StrCpy $0 "$0\${PRODUCT_INSTALL_DIR}"
        ${EndIf}
        ${NSD_SetText} $Txt_Browser  $0
    ${EndIf}
FunctionEnd
Function onClickexpress
    Exec "$INSTDIR\${PRODUCT_EXE}"
    SendMessage $hwndparent ${WM_CLOSE} 0 0
FunctionEnd
Function onClickend
    SendMessage $hwndparent ${WM_CLOSE} 0 0
FunctionEnd
Section Uninstall
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "WarrenQAIService"
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /T /IM WarrenQAIService.exe'
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /T /IM WarrenqAILauncher.exe'
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /T /IM WarrenqF1.Desktop.exe'
    Sleep 500
    ${If} ${ISREMOVEUSERS} == '1'
        Delete "$INSTDIR\*.*"
        RMDir /r "$INSTDIR"
    ${Else}
        MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "您是否需要保留用户资料以及相关配置信息？" IDYES YES IDNO NO
        YES:
        nsExec::ExecToStack '"$INSTDIR\Excel\Excel\RegTool.exe" /unregister'
        RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
        RMDir /r "$INSTDIR\Excel"
        RMDir /r "$INSTDIR\QuantAPI"
        RMDir /r "$INSTDIR\WarrenqAIEngine"
        RMDir /r "$INSTDIR\update"
        Delete "$INSTDIR\*.*"
        goto CONTINUE
        NO:
        nsExec::ExecToStack '"$INSTDIR\Excel\Excel\RegTool.exe" /unregister'
        RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
        RMDir /r "$PROFILE\.warrenqai"
        RMDir /r "$APPDATA\WarrenqF1"
        RMDir /r "$INSTDIR"
    ${EndIf}
    goto CONTINUE
    CONTINUE:
    SetShellVarContext all
    Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
    Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk"
    Delete /REBOOTOK "$DESKTOP\${PRODUCT_NAME}.lnk"
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}\*.*"
    RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
    DeleteRegKey HKLM "${PRODUCT_UNINST_KEY}"
    DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
    SetAutoClose true
SectionEnd
