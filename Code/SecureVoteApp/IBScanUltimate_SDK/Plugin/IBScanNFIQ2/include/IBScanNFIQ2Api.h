/*
****************************************************************************************************
* IBScanNFIQ2.h
*
* DESCRIPTION:
*     API functions for IBScanNFIQ2.
*     http://www.integratedbiometrics.com
*
* NOTES:
*     Copyright (c) Integrated Biometrics, 2018-2018
*
* HISTORY:
*     2018/01/03  1.0.0  Created.
****************************************************************************************************
*/

#pragma once

#include "IBScanNFIQ2Api_err.h"

#ifdef __cplusplus
extern "C" { 
#endif

/*
****************************************************************************************************
* GLOBAL FUNCTIONS
****************************************************************************************************
*/

/*
****************************************************************************************************
* IBSU_NFIQ2_GetVersion()
* 
* DESCRIPTION:
*     Obtain product IB NFIQ2 Wrapper version information.
*
* ARGUMENTS:
*     pVerinfo  Pointer to string that will receive IB NFIQ2 DLL Wrapper version information.
*
* RETURNS:
*     IBSU_STATUS_OK, if successful.
*     Error code < 0, otherwise.  See error codes in 'IBScanUltimateApi_err'.
****************************************************************************************************
*/
int WINAPI IBSU_NFIQ2_GetVersion
    (char *pVerinfo);

/*
****************************************************************************************************
* IBSU_NFIQ2_GetNISTVersion()
*
* DESCRIPTION:
*     Obtain NIST NFIQ2 official version
*
* ARGUMENTS:
*     pVerinfo  Pointer to string that will receive NIST NFIQ2 DLL version information.
*
* RETURNS:
*     IBSU_STATUS_OK, if successful.
*     Error code < 0, otherwise.  See error codes in 'IBScanUltimateApi_err'.
****************************************************************************************************
*/
int WINAPI IBSU_NFIQ2_GetNISTVersion
        (char *pVerinfo);

/*
****************************************************************************************************
* IBSU_NFIQ2_Initialize()
* 
* DESCRIPTION:
*     Initialize NFIQ2 module. It may takes few seconds depend on CPU
*
* ARGUMENTS:
*
* RETURNS:
*     IBSU_STATUS_OK, if successful.
*     Error code < 0, otherwise.  See error codes in 'IBScanUltimateApi_err'.
****************************************************************************************************
*/
int WINAPI IBSU_NFIQ2_Initialize();


/*
****************************************************************************************************
* IBSU_NFIQ2_IsInitialized()
* 
* DESCRIPTION:
*     Check whether a NFIQ module is initialized.
*
* ARGUMENTS:
*
* RETURNS:
*     IBSU_STATUS_OK, if NFIQ module is initialized.
*     Error code < 0, otherwise.  See error codes in 'IBScanUltimateApi_err'.
****************************************************************************************************
*/
int WINAPI IBSU_NFIQ2_IsInitialized();

/*
****************************************************************************************************
* IBSU_NFIQ2_ComputeScore()
* 
* DESCRIPTION:
*     Calculate NFIQ2 score for image.
*
* ARGUMENTS:
*     imgBuffer     Pointer to image buffer.
*     width         Image width (in pixels).
*     height        Image height (in pixels).
*     bitsPerPixel  Bits per pixel.
*     pScore        Pointer to variable that will receive NFIQ score.
*
* RETURNS:
*     IBSU_STATUS_OK, if successful.
*     Error code < 0, otherwise.  See error codes in 'IBScanUltimateApi_err'.
****************************************************************************************************
*/
int WINAPI IBSU_NFIQ2_ComputeScore
    (const BYTE *imgBuffer,
     const DWORD width,
     const DWORD height,
     const BYTE  bitsPerPixel,
     int        *pScore);


#ifdef __cplusplus
} // extern "C"
#endif

