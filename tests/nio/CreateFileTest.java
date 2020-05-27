package semtests;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public class CreateFileTest {

    public static void main(String... args) throws IOException {
        System.out.println("Running");        
        Path dir = Files.createTempDirectory("my-dir");
        Path fileToCreatePath = dir.resolve("test-file.txt");
        System.out.println("File to create path: " + fileToCreatePath);
        Path newFilePath = Files.createFile(fileToCreatePath);
        System.out.println("New file created: " + newFilePath);
        System.out.println("New File exits: " + Files.exists(newFilePath));
    }
}