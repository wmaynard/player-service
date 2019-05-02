package com.rumble.platform

import java.nio.file.Files
import java.nio.file.Paths

class RumblePath {
    static boolean isLocalPath(path) {
        final String protocol = this.getProtocol(path)
        return "file".equalsIgnoreCase(protocol)
    }

    /**
     * Returns the protocol for a given URI or filename.
     * Source: https://stackoverflow.com/questions/6575114/how-do-i-determine-whether-a-path-is-a-local-file-or-not
     *
     * @param source Determine the protocol for this URI or filename.
     *
     * @return The protocol for the given source.
     */
    private static String getProtocol( final String source ) {
        assert source != null;

        String protocol = null;

        try {
            final URI uri = new URI( source );

            if( uri.isAbsolute() ) {
                protocol = uri.getScheme();
            }
            else {
                final URL url = new URL( source );
                protocol = url.getProtocol();
            }
        } catch( final Exception e ) {
            // Could be HTTP, HTTPS?
            if( source.startsWith( "//" ) ) {
                throw new IllegalArgumentException( "Relative context: " + source );
            }
            else {
                final File file = new File( source );
                protocol = getProtocol( file );
            }
        }

        return protocol;
    }

    /**
     * Returns the protocol for a given file.
     * Source: https://stackoverflow.com/questions/6575114/how-do-i-determine-whether-a-path-is-a-local-file-or-not
     *
     * @param file Determine the protocol for this file.
     *
     * @return The protocol for the given file.
     */
    private static String getProtocol( final File file ) {
        String result;

        try {
            result = file.toURI().toURL().getProtocol();
        } catch( Exception e ) {
            result = "unknown";
        }

        return result;
    }

    static String readFile(String path) throws IOException {
        byte[] encoded = Files.readAllBytes(Paths.get(path))
        return new String(encoded)
    }
}